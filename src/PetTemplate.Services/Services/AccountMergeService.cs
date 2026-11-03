using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Services;

/// <summary>
/// Слияние аккаунтов (спец. accounts, раздел 7). Владение обоими аккаунтами доказано
/// до вызова: текущим — свежим входом (8.6), вторым — паролем или подписанным входом (7.1).
/// Здесь только просмотр с ключом и само слияние.
///
/// Ключ слияния живёт в памяти процесса на <see cref="AccountOptions.MergeTicketTtl"/>
/// (10 минут) и одноразовый: перезапуск сервера между просмотром и подтверждением
/// просто попросит просмотреть ещё раз. API один (<c>tech-debt/single-instance.md</c>)
/// </summary>
public class AccountMergeService(
    ApplicationContext db,
    AccountCards cards,
    AccountDeletion deletion,
    SessionGuard sessions,
    IMemoryCache cache,
    IOptions<AccountOptions> options)
{
    /// <summary>
    /// Ключ слияния: кто с кем, чем доказано владение вторым и снимок состава пространств
    /// обоих аккаунтов на момент просмотра
    /// </summary>
    public sealed record Ticket(
        Guid CurrentUserId,
        Guid OtherUserId,
        string Platform,
        string[] CurrentComposition,
        string[] OtherComposition);

    private static string Key(Guid ticket) => $"merge-ticket:{ticket}";

    /// <summary>
    /// Обе карточки (7.3) и ключ на подтверждение. <paramref name="platform"/> — чем
    /// доказано владение вторым (пароль, Telegram, VK): уходит в журнал слияния.
    ///
    /// Аккаунт с ролью админки удалить нельзя (раздел 10) — при слиянии он может только
    /// остаться, оба с ролями — <c>400 auth.error.adminCannotBeDeleted</c> здесь же, до подтверждения
    /// </summary>
    public async Task<MergePreviewDto> Preview(Guid currentUserId, Guid otherUserId, string platform, CancellationToken ct = default)
    {
        // Оба с ролями — слить их нельзя никак, и это видно сразу, а не после подтверждения.
        // Один — карточка говорит об этом (CanBeRemoved), и экран не даёт выбрать его удаляемым
        if (await db.UserInAdminRoles.Where(x => x.UserId == currentUserId || x.UserId == otherUserId)
                .Select(x => x.UserId).Distinct().CountAsync(ct) == 2)
        {
            throw new BadRequestException(AuthMessages.AdminCannotBeDeleted);
        }

        var ticketId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow + options.Value.MergeTicketTtl;

        var ticket = new Ticket(
            currentUserId,
            otherUserId,
            platform,
            await cards.Composition(currentUserId, ct),
            await cards.Composition(otherUserId, ct));
        cache.Set(Key(ticketId), ticket, expiresAt);

        return new MergePreviewDto
        {
            Ticket = ticketId,
            ExpiresAtUtc = expiresAt,
            Current = await cards.Build(currentUserId, ct),
            Other = await cards.Build(otherUserId, ct),
            CanKeepOther = await CanKeep(otherUserId, ct)
        };
    }

    /// <summary>
    /// Забрать ключ — один раз и только тому, кто его получил. Чужой, просроченный или
    /// уже использованный — <c>null</c>
    /// </summary>
    public Ticket? TakeTicket(Guid ticketId, Guid currentUserId)
    {
        if (!cache.TryGetValue(Key(ticketId), out Ticket? ticket) || ticket is null) return null;
        if (ticket.CurrentUserId != currentUserId) return null;

        cache.Remove(Key(ticketId));
        return ticket;
    }

    /// <summary>
    /// Слияние (7.4). Ключ — тот, что выдал просмотр этому же человеку; <paramref name="keepUserId"/> —
    /// один из двух аккаунтов. Одна транзакция, по порядку:
    ///
    /// <list type="number">
    /// <item>права удаляемого в общих пространствах переходят остающемуся — объединением
    /// наборов: слияние не отнимает прав, которые были у любого из аккаунтов (7.2.1);</item>
    /// <item>Telegram и VK удаляемого переходят остающемуся, если у того место свободно;</item>
    /// <item>удаляемый удаляется общим путём (<see cref="AccountDeletion"/>): у него
    /// остались только пространства, где он один, — они удаляются.</item>
    /// </list>
    ///
    /// Авторство (приглашения, сущности проекта) на остающегося не перебивается (7.2.1):
    /// удаляемый уходит как любой другой, ссылка пустеет. Состав пространств поменялся
    /// с просмотра или одного из аккаунтов уже нет — <c>409</c>: проверяется внутри
    /// транзакции, под замком строк обоих пользователей. Остающийся второй моложе суток —
    /// <c>400</c> (<see cref="CanKeep"/>). Ответ — id остающегося: ему вызывающий выдаёт
    /// новую сессию
    /// </summary>
    public async Task<Guid> Merge(Guid currentUserId, Guid ticketId, Guid keepUserId, CancellationToken ct = default)
    {
        var ticket = TakeTicket(ticketId, currentUserId) ?? throw new BadRequestException(AuthMessages.MergeTicketInvalid);
        if (keepUserId != ticket.CurrentUserId && keepUserId != ticket.OtherUserId)
        {
            throw new BadRequestException(AuthMessages.MergeTicketInvalid);
        }

        var removedId = keepUserId == ticket.CurrentUserId ? ticket.OtherUserId : ticket.CurrentUserId;

        if (keepUserId == ticket.OtherUserId && !await CanKeep(keepUserId, ct))
        {
            throw new BadRequestException(AuthMessages.MergeKeepYoungAccount);
        }

        // Удаление проверит это и само, но уже после переноса прав — отказ без отката лучше
        if (await db.UserInAdminRoles.AnyAsync(x => x.UserId == removedId, ct))
        {
            throw new BadRequestException(AuthMessages.AdminCannotBeDeleted);
        }

        try
        {
            await Run(ticket, removedId, keepUserId, ct);
        }
        catch (Exception ex) when (DbErrors.IsLockConflict(ex))
        {
            // Одновременное второе слияние тех же аккаунтов: база выбрала эту транзакцию
            // жертвой взаимной блокировки и откатила её — для человека это та же перемена
            // «с тех пор, как смотрели», что и 409 ниже
            throw new ConflictException(AuthMessages.MergeChanged);
        }

        // Удаление внутри транзакции слияния сбросило кэш удаляемого до коммита — запрос,
        // пришедший в это окно, мог снова положить туда «жив». Сбрасываем повторно
        sessions.Forget(removedId);
        sessions.Forget(keepUserId);
        return keepUserId;
    }

    /// <summary>
    /// Второй аккаунт может остаться, удалив текущий, только если заведён не меньше
    /// <see cref="AccountOptions.MergeKeepOtherMinAge"/> назад (7.3). Украденной сессии иначе
    /// хватило бы, чтобы слить аккаунт жертвы в только что заведённый свой и забрать её
    /// пространства. Обратное — остаётся текущий — так не ограничено: в худшем случае жертва
    /// получает чужие данные, а не теряет свои
    /// </summary>
    private async Task<bool> CanKeep(Guid otherUserId, CancellationToken ct)
    {
        var createdAt = await db.Users.Where(x => x.Id == otherUserId).Select(x => x.CreatedAtUtc).SingleAsync(ct);
        return DateTime.UtcNow - createdAt >= options.Value.MergeKeepOtherMinAge;
    }

    /// <summary>Само слияние — одной транзакцией под замком строк обоих пользователей</summary>
    private async Task Run(Ticket ticket, Guid removedId, Guid keepUserId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // READ COMMITTED — как у удаления (AccountDeletion.Lock): после замков читается
            // то, что есть сейчас, а не снимок с начала транзакции
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Оба аккаунта живы — и заперты до конца транзакции: параллельное удаление
            // или второе слияние с ними же подождёт. Второй удалили с просмотра — 409,
            // как при любой другой перемене, а не 500 на пустой выборке
            if (await LockUsers(ticket.CurrentUserId, ticket.OtherUserId, ct) != 2
                || !(await cards.Composition(ticket.CurrentUserId, ct)).SequenceEqual(ticket.CurrentComposition)
                || !(await cards.Composition(ticket.OtherUserId, ct)).SequenceEqual(ticket.OtherComposition))
            {
                throw new ConflictException(AuthMessages.MergeChanged);
            }

            var removedName = await db.Profiles.Where(x => x.Id == removedId).Select(x => x.UserName).SingleAsync(ct);
            var keptName = await db.Profiles.Where(x => x.Id == keepUserId).Select(x => x.UserName).SingleAsync(ct);

            await MovePermissions(removedId, keepUserId, ct);
            await MoveLogins(removedId, keepUserId, ct);
            await MoveFacts(removedId, keepUserId, ct);

            await deletion.Delete(removedId, ct, journal: context => context.AccountMerges.Add(new AccountMerge
            {
                Id = Guid.CreateVersion7(),
                KeptUserId = keepUserId,
                RemovedUserId = removedId,
                RemovedUserName = removedName,
                KeptUserName = keptName,
                Platform = ticket.Platform,
                CreatedUtc = DateTime.UtcNow
            }));

            await transaction.CommitAsync(ct);
        });
    }

    /// <summary>
    /// Запереть строки обоих пользователей до конца транзакции. Ответ — сколько из них есть:
    /// удалённого уже нет, и замок он не держит
    /// </summary>
    private async Task<int> LockUsers(Guid first, Guid second, CancellationToken ct) =>
        (await db.Users
            .FromSql($"SELECT * FROM users WHERE id IN ({first}, {second}) ORDER BY id FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(ct)).Count;

    /// <summary>
    /// Права удаляемого в общих пространствах (где есть кто-то кроме него — в том числе
    /// сам остающийся) — остающемуся, объединением наборов: строка, которая у остающегося
    /// уже есть, не дублируется (ключ — пространство, человек, право). Пространства, где
    /// удаляемый один, не передаются: они удалятся вместе с ним (7.2.1).
    ///
    /// Основное пространство остающегося не меняется; не было никакого — им становится
    /// первое перешедшее, как первое созданное (спец. authorization, 7.1)
    /// </summary>
    private async Task MovePermissions(Guid removedId, Guid keepId, CancellationToken ct)
    {
        var removed = await db.UserInPermissions.Where(x => x.UserId == removedId).ToListAsync(ct);
        var workspaceIds = removed.Select(x => x.WorkspaceId).Distinct().ToList();
        var shared = await db.UserInPermissions
            .Where(x => workspaceIds.Contains(x.WorkspaceId) && x.UserId != removedId)
            .Select(x => x.WorkspaceId)
            .Distinct()
            .ToListAsync(ct);
        if (shared.Count == 0) return;

        var kept = await db.UserInPermissions
            .Where(x => x.UserId == keepId && shared.Contains(x.WorkspaceId))
            .Select(x => new { x.WorkspaceId, x.PermissionId })
            .ToListAsync(ct);

        foreach (var permission in removed.Where(x => shared.Contains(x.WorkspaceId)))
        {
            if (!kept.Any(x => x.WorkspaceId == permission.WorkspaceId && x.PermissionId == permission.PermissionId))
            {
                db.UserInPermissions.Add(new UserInPermission
                {
                    WorkspaceId = permission.WorkspaceId,
                    UserId = keepId,
                    PermissionId = permission.PermissionId
                });
            }

            db.UserInPermissions.Remove(permission);
        }

        var keptProfile = await db.Profiles.SingleAsync(x => x.Id == keepId, ct);
        keptProfile.PrimaryWorkspaceId ??= shared.Order().First();

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Личная память ассистента удаляемого (спец. assistant, 12.5) — остающемуся: факты
    /// «про меня» записаны со слов того же человека. Переезжает в каждом пространстве,
    /// а там, где удаляемый был один, её всё равно унесёт снос пространства
    /// (<see cref="WorkspaceService.PurgeContents"/>). Совпал ключ с фактом остающегося —
    /// остаётся его факт: дедупликация по ключу (пространство, владелец, вид, ключ),
    /// и второго такого быть не может. Без внешнего ключа переехать нечему — без этого шага
    /// каскад удаления аккаунта стёр бы их вместе с удаляемым
    /// </summary>
    private async Task MoveFacts(Guid removedId, Guid keepId, CancellationToken ct)
    {
        var removed = await db.AssistantFacts.Where(x => x.UserId == removedId).ToListAsync(ct);
        if (removed.Count == 0) return;

        var kept = await db.AssistantFacts
            .Where(x => x.UserId == keepId)
            .Select(x => new { x.WorkspaceId, x.Kind, x.Key })
            .ToListAsync(ct);

        foreach (var fact in removed)
        {
            if (kept.Any(x => x.WorkspaceId == fact.WorkspaceId && x.Kind == fact.Kind && x.Key == fact.Key))
            {
                db.AssistantFacts.Remove(fact);
            }
            else
            {
                fact.UserId = keepId;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Telegram и VK удаляемого — остающемуся, если место свободно; иначе пропадают вместе
    /// с удаляемым (7.2). Сначала снимаются у удаляемого: уникальный индекс не даст двум
    /// аккаунтам один и тот же вход даже на миг. Переехавший вход — свежепривязанный
    /// (<see cref="User.TelegramLinkedAtUtc"/>): слить в аккаунт свой Telegram мог и тот,
    /// кто украл сессию, — сутки он не задаёт пароль без старого и не подтверждает владение (8.3, 8.6)
    /// </summary>
    private async Task MoveLogins(Guid removedId, Guid keepId, CancellationToken ct)
    {
        var removed = await db.Users.SingleAsync(x => x.Id == removedId, ct);
        var keep = await db.Users.SingleAsync(x => x.Id == keepId, ct);

        var telegram = keep.TelegramId == null ? removed.TelegramId : null;
        var vk = keep.VkId == null ? removed.VkId : null;
        if (telegram == null && vk == null) return;

        if (telegram != null) removed.TelegramId = null;
        if (vk != null) removed.VkId = null;
        await db.SaveChangesAsync(ct);

        // Одна отметка на оба входа: переехали одним слиянием
        var now = DateTime.UtcNow;
        if (telegram != null)
        {
            keep.TelegramId = telegram;
            keep.TelegramLinkedAtUtc = now;
        }

        if (vk != null)
        {
            keep.VkId = vk;
            keep.VkLinkedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
