using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Services.Services;

/// <summary>
/// Удаление аккаунта (спец. accounts, раздел 10) — один путь для профиля (раздел 9),
/// админки (8.5) и слияния (7.4). Физическое: строки <c>User</c> и <c>Profile</c>
/// уходят из базы. Одна транзакция, по порядку:
///
/// <list type="number">
/// <item>общие пространства — выход по правилам спец. authorization, 8
/// (<see cref="WorkspaceService.Leave"/>): право управлять участниками передаётся оставшимся,
/// основное пространство — следующему;</item>
/// <item>пространства, где он один, — сразу, тем же порядком, что фоновая чистка
/// (<see cref="WorkspaceService.PurgeContents"/>): человек сам выбрал удаление и видел,
/// что пропадёт;</item>
/// <item>строка <c>User</c>; остальное делают внешние ключи в базе: <c>Profile</c>, права,
/// токены аккаунта — каскадом; авторство приглашений (<c>CreatedBy</c>, <c>AcceptedBy</c>)
/// и журналы — обнулением ссылки.</item>
/// </list>
///
/// Сущность, которую проект добавит поверх шаблона, выбирает одно из двух: авторство —
/// нуллабельный ключ с <c>SetNull</c>, личное — <c>Cascade</c>. Здесь дописывать ничего не
/// нужно — кроме личного без внешнего ключа (например, личные чаты): его удаляет шаг до
/// удаления строки <c>User</c>.
///
/// Письма в очереди (<c>SendMailCommand</c>) не трогаются: письмо со ссылкой ссылается на токен,
/// токен ушёл каскадом — обработчик его не найдёт и письмо не отправит.
///
/// Логин, почта, телефон, <c>TelegramId</c> и <c>VkId</c> освобождаются сразу. Токены
/// перестают действовать: <see cref="SessionGuard"/> не находит пользователя и отвечает <c>401</c>
/// </summary>
public class AccountDeletion(
    ApplicationContext db,
    WorkspaceService workspaces,
    SessionGuard sessions)
{
    /// <param name="journal">
    /// Запись журнала, которая должна уйти той же транзакцией: слияние (7.4) и удаление
    /// из админки (8.5) пишут, кто и зачем удалил
    /// </param>
    /// <param name="precondition">
    /// Проверка вызывающего — под теми же замками, что и удаление, а не до транзакции: админка
    /// удаляет только аккаунт без пространств (8.5), и пространство, созданное между проверкой
    /// и удалением, иначе стёрлось бы как «только его»
    /// </param>
    public async Task Delete(Guid userId, CancellationToken ct = default, Action<ApplicationContext>? journal = null,
        Func<CancellationToken, Task>? precondition = null)
    {
        // Слияние зовёт удаление внутри своей транзакции (7.4) — тогда всё идёт в ней
        if (db.Database.CurrentTransaction != null)
        {
            await Steps(userId, journal, precondition, ct);
        }
        else
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                // READ COMMITTED (у Postgres — по умолчанию): чтения после замков видят то,
                // что закоммичено к этой минуте, а не снимок с начала транзакции (см. Lock)
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await Steps(userId, journal, precondition, ct);
                await transaction.CommitAsync(ct);
            });
        }

        db.ChangeTracker.Clear();
        sessions.Forget(userId);
    }

    /// <summary>
    /// Шаги удаления — внутри транзакции и под замками (<see cref="Lock"/>): список «где он
    /// один» читается после них. Иначе пространство, куда между проверкой и удалением
    /// приняли второго участника, снеслось бы как «только его», а из пространства, откуда
    /// в это время ушёл последний другой, удаляемый вышел бы — и оно осталось бы ничьим
    /// </summary>
    private async Task Steps(Guid userId, Action<ApplicationContext>? journal,
        Func<CancellationToken, Task>? precondition, CancellationToken ct)
    {
        await Lock(userId, ct);
        if (precondition != null) await precondition(ct);

        // Аккаунт с ролью админки не удаляется (раздел 10): сначала роли снимает другой админ.
        // Под замком — общий путь профиля, слияния и админки проверяет это одним местом
        if (await db.UserInAdminRoles.AnyAsync(x => x.UserId == userId, ct))
        {
            throw new BadRequestException(AuthMessages.AdminCannotBeDeleted);
        }

        var memberships = await db.UserInPermissions
            .Where(x => x.UserId == userId)
            .Select(x => x.WorkspaceId)
            .Distinct()
            .ToListAsync(ct);
        var shared = await db.UserInPermissions
            .Where(x => memberships.Contains(x.WorkspaceId) && x.UserId != userId)
            .Select(x => x.WorkspaceId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var workspaceId in shared)
        {
            await workspaces.Leave(userId, workspaceId, ct);
        }

        foreach (var workspaceId in memberships.Except(shared))
        {
            // Права на пространство стоят Restrict: сначала его строки, потом оно само
            await db.UserInPermissions.Where(x => x.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
            await workspaces.PurgeContents(workspaceId, ct);
        }

        await db.Users.Where(x => x.Id == userId).ExecuteDeleteAsync(ct);

        if (journal != null)
        {
            journal(db);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Замки до конца транзакции:
    /// <list type="bullet">
    /// <item>строка пользователя — новое право с ним ссылается на неё внешним ключом и ждёт;</item>
    /// <item>его пространства — приём нового участника (приглашение, выдача прав) ссылается
    /// на пространство внешним ключом и ждёт;</item>
    /// <item>права в его пространствах — выход и удаление участника снимают эти строки: тоже ждут.</item>
    /// </list>
    /// Читается после замков под READ COMMITTED — то есть то, что есть сейчас
    /// </summary>
    private async Task Lock(Guid userId, CancellationToken ct)
    {
        await db.Users
            .FromSql($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(ct);
        await db.Workspaces
            .FromSql($"""
                SELECT * FROM workspaces
                WHERE id IN (SELECT workspace_id FROM user_in_permissions WHERE user_id = {userId})
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(ct);
        await db.UserInPermissions
            .FromSql($"""
                SELECT * FROM user_in_permissions
                WHERE workspace_id IN (SELECT workspace_id FROM user_in_permissions WHERE user_id = {userId})
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
