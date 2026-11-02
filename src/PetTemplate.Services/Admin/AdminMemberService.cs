using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Админы и их роли (спец. admin, 3.1 и 3.4): список, выдача, снятие и удаление.
/// Инварианты те же, что у прав пространства:
///
/// <list type="bullet">
/// <item>нельзя выдать роль, которой нет у себя — иначе роль одного раздела
/// выписывала бы себе роль другого в два клика;</item>
/// <item>хотя бы один с <c>AdminManagement</c> остаётся — вернуть её в систему было
/// бы некому, кроме строки в базе;</item>
/// <item>снятие последней роли — не молчаливое «минус роль», а явное удаление админа.</item>
/// </list>
///
/// Свои роли выдающего читаются из базы, а не из токена. Снятую роль проверка токена
/// и так отбрасывает на каждом запросе (3.3), но читать её здесь из токена незачем.
///
/// Гонку двух админов, снимающих <c>AdminManagement</c> друг у друга в одну секунду,
/// сервис не держит: админов один-два, а последствие чинится тем же, чем заводился
/// первый, — строкой в базе (3.1).
///
/// Снятие роли и удаление админа строку стирают, и «кто это сделал» (3.5) хранить
/// в ней негде — оно уходит в лог приложения: кто, у кого, какие роли.
///
/// Отказы — текстом, без кодов: админка их не переводит (3.5)
/// </summary>
public class AdminMemberService(ApplicationContext db, ILogger<AdminMemberService> logger)
{
    public async Task<AdminMemberDto[]> List(CancellationToken ct = default)
    {
        var rows = await db.UserInAdminRoles
            .AsNoTracking()
            .Select(x => new
            {
                x.UserId,
                UserName = db.Profiles
                    .Where(p => p.Id == x.UserId)
                    .Select(p => p.UserName)
                    .FirstOrDefault(),
                x.Role,
                x.GrantedUtc,
                // Выдавший мог уйти из системы — след выдачи остаётся без имени
                GrantedByName = db.Profiles
                    .Where(p => p.Id == x.GrantedByUserId)
                    .Select(p => p.UserName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => new { x.UserId, x.UserName })
            .Select(x => new AdminMemberDto
            {
                UserId = x.Key.UserId,
                UserName = x.Key.UserName ?? x.Key.UserId.ToString(),
                Roles = x
                    .OrderBy(r => r.Role)
                    .Select(r => new AdminMemberRoleDto
                    {
                        Role = r.Role,
                        GrantedUtc = r.GrantedUtc,
                        GrantedByName = r.GrantedByName
                    })
                    .ToArray()
            })
            .OrderBy(x => x.UserName)
            .ToArray();
    }

    /// <summary>
    /// Ещё одна роль тому, кто уже админ. Заводить админа мимо приглашения этот метод
    /// не должен: у неадмина строк нет, и такой цели здесь просто не находится.
    /// <c>false</c> — такого админа нет
    /// </summary>
    public async Task<bool> Grant(Guid userId, AdminRole role, Guid actorId, CancellationToken ct = default)
    {
        if (!AdminRoleSet.IsSingle(role)) throw new ArgumentException("Неизвестная роль");

        var own = await EnsureManager(actorId, ct);
        if (!own.Contains(role)) throw new ArgumentException("Нельзя выдать роль, которой нет у вас самих");

        var current = await RolesOf(userId, ct);
        if (current.Length == 0) return false;

        // Повторная выдача ничего не переписывает: след первой остаётся
        if (current.Contains(role)) return true;

        db.UserInAdminRoles.Add(new UserInAdminRole
        {
            UserId = userId,
            Role = role,
            GrantedUtc = DateTime.UtcNow,
            GrantedByUserId = actorId
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsDuplicate(e, DbErrors.AdminRoleKey))
        {
            // Двойной клик: вторая выдача упёрлась в ключ (UserId, Role), который только
            // что записала первая. Роль у человека есть — это и есть ответ
        }

        return true;
    }

    /// <summary>
    /// Снимает одну роль. Последнюю так не снять: человек перестал бы быть админом,
    /// а это — отдельное явное действие (<see cref="Remove"/>). <c>false</c> — такого админа нет
    /// </summary>
    public async Task<bool> Revoke(Guid userId, AdminRole role, Guid actorId, CancellationToken ct = default)
    {
        await EnsureManager(actorId, ct);

        var current = await RolesOf(userId, ct);
        if (current.Length == 0) return false;

        // Снятой уже нет — снимать нечего
        if (!current.Contains(role)) return true;

        if (current.Length == 1)
            throw new ArgumentException("Это последняя роль: без ролей человек не админ — удалите его явно");

        if (role == AdminRole.AdminManagement) await EnsureNotLastManager(userId, ct);

        db.UserInAdminRoles.RemoveRange(
            await db.UserInAdminRoles.Where(x => x.UserId == userId && x.Role == role).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Admin {ActorId} revoked role {Role} from admin {UserId}", actorId, role, userId);
        return true;
    }

    /// <summary>
    /// Все роли разом — человек уходит из админки, аккаунт в продукте остаётся.
    /// <c>false</c> — такого админа нет
    /// </summary>
    public async Task<bool> Remove(Guid userId, Guid actorId, CancellationToken ct = default)
    {
        await EnsureManager(actorId, ct);

        var current = await RolesOf(userId, ct);
        if (current.Length == 0) return false;

        if (current.Contains(AdminRole.AdminManagement)) await EnsureNotLastManager(userId, ct);

        db.UserInAdminRoles.RemoveRange(await db.UserInAdminRoles.Where(x => x.UserId == userId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Admin {ActorId} removed admin {UserId} with roles {Roles}", actorId, userId, string.Join(',', current));
        return true;
    }

    /// <summary>
    /// Роли выдающего из базы, а не из токена: здесь снятие видно в том же запросе
    /// </summary>
    internal async Task<AdminRole[]> EnsureManager(Guid actorId, CancellationToken ct)
    {
        var own = await RolesOf(actorId, ct);

        if (!own.Contains(AdminRole.AdminManagement))
            throw new ArgumentException("Роль AdminManagement у вас уже снята — войдите заново");

        return own;
    }

    internal async Task<AdminRole[]> RolesOf(Guid userId, CancellationToken ct) =>
        await db.UserInAdminRoles
            .Where(x => x.UserId == userId)
            .Select(x => x.Role)
            .ToArrayAsync(ct);

    private async Task EnsureNotLastManager(Guid userId, CancellationToken ct)
    {
        var others = await db.UserInAdminRoles
            .AnyAsync(x => x.Role == AdminRole.AdminManagement && x.UserId != userId, ct);

        if (!others)
            throw new ArgumentException("Это последний, у кого есть AdminManagement: вернуть роль в систему будет некому");
    }
}
