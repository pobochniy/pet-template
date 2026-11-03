using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Аккаунты в админке (спец. accounts, 8.5; admin, раздел 12): поиск, карточка для
/// проверки владельца, ссылка сброса пароля, освобождение почты и телефона, удаление
/// аккаунта без пространств, журнал. Поддержка бесплатная — ничего
/// не списывается.
///
/// Отказы — текстом, без кодов: админка их не переводит (спец. admin, 3.5)
/// </summary>
public class AdminAccountService(
    ApplicationContext db,
    AccountCards cards,
    AccountDeletion deletion,
    AccountTokens tokens,
    IOptions<AccountOptions> account,
    IOptions<MailOptions> mail)
{
    private const int SearchLimit = 20;

    /// <summary>Страница журнала</summary>
    internal const int JournalPageSize = 100;

    /// <summary>
    /// По логину (часть, без учёта регистра), почте и телефону (часть), Telegram или VK
    /// (id целиком — его называет человек из своего профиля площадки). Логин и почта —
    /// <c>citext</c> (спец. infrastructure, 3.1), <c>ILIKE</c> по ним регистр не различает
    /// </summary>
    public async Task<AdminAccountDto[]> Search(string? query, CancellationToken ct = default)
    {
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text)) return [];

        var name = Like(text.TrimStart('@'));
        var contact = Like(text);
        var found = long.TryParse(text, out var id)
            ? db.Profiles.Where(x => x.User!.TelegramId == id || x.User.VkId == id
                                     || EF.Functions.ILike(x.UserName, name, Escape)
                                     || (x.PhoneNumber != null && EF.Functions.ILike(x.PhoneNumber, contact, Escape)))
            : db.Profiles.Where(x => EF.Functions.ILike(x.UserName, name, Escape)
                                     || (x.Email != null && EF.Functions.ILike(x.Email, contact, Escape))
                                     || (x.PhoneNumber != null && EF.Functions.ILike(x.PhoneNumber, contact, Escape)));

        return await found
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .Take(SearchLimit)
            .Select(x => new AdminAccountDto
            {
                UserId = x.Id,
                UserName = x.UserName,
                Email = x.Email,
                PhoneNumber = x.PhoneNumber,
                HasTelegram = x.User!.TelegramId != null,
                HasVk = x.User.VkId != null,
                RegisteredAtUtc = x.User.CreatedAtUtc
            })
            .ToArrayAsync(ct);
    }

    /// <summary>Карточка; аккаунта нет — <c>null</c></summary>
    public async Task<AdminAccountCardDto?> Card(Guid userId, CancellationToken ct = default)
    {
        var profile = await db.Profiles
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Email, x.EmailConfirmed, x.PhoneNumber, x.User!.LastLoginAtUtc })
            .SingleOrDefaultAsync(ct);
        if (profile == null) return null;

        var actions = await db.AccountAdminActions
            .AsNoTracking()
            .Where(x => x.TargetUserId == userId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new AdminAccountActionDto
            {
                Kind = x.Kind,
                AdminName = db.Profiles.Where(p => p.Id == x.AdminUserId).Select(p => p.UserName).FirstOrDefault(),
                Reason = x.Reason,
                CreatedUtc = x.CreatedUtc
            })
            .ToListAsync(ct);

        return new AdminAccountCardDto
        {
            Card = await cards.Build(userId, ct),
            Email = profile.Email,
            EmailConfirmed = profile.EmailConfirmed,
            PhoneNumber = profile.PhoneNumber,
            LastLoginAtUtc = profile.LastLoginAtUtc,
            IsAdmin = await db.UserInAdminRoles.AnyAsync(x => x.UserId == userId, ct),
            Actions = actions
        };
    }

    /// <summary>
    /// Журнал (спец. accounts, 8.5 и 11): действия админов с чужими аккаунтами и слияния
    /// одной лентой, новые сверху, страницами. Карточка удалённого аккаунта — 404, и запись
    /// о его удалении или слиянии видна только здесь.
    ///
    /// Поиск — по логину (часть, без учёта регистра) или по id аккаунта целиком. Логины
    /// цели и обоих аккаунтов слияния — копии в журнале, аккаунт они переживают
    /// </summary>
    public async Task<AdminAccountJournalPageDto> Journal(AdminAccountJournalFilterDto filter, CancellationToken ct = default)
    {
        var actions = db.AccountAdminActions.AsNoTracking();
        var merges = db.AccountMerges.AsNoTracking();

        var text = filter.Query?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (Guid.TryParse(text, out var userId))
            {
                actions = actions.Where(x => x.TargetUserId == userId);
                merges = merges.Where(x => x.RemovedUserId == userId || x.KeptUserId == userId);
            }
            else
            {
                var name = Like(text.TrimStart('@'));
                actions = actions.Where(x => EF.Functions.ILike(x.TargetUserName, name, Escape));
                merges = merges.Where(x => EF.Functions.ILike(x.RemovedUserName, name, Escape)
                                           || EF.Functions.ILike(x.KeptUserName, name, Escape));
            }
        }

        // Курсор — пара (время, Id) из любой из двух таблиц: записи с одной меткой иначе
        // терялись бы на границе страницы
        if (filter.Before is { } before)
        {
            var cursor = await db.AccountAdminActions.Where(x => x.Id == before).Select(x => new { x.CreatedUtc, x.Id }).SingleOrDefaultAsync(ct)
                         ?? await db.AccountMerges.Where(x => x.Id == before).Select(x => new { x.CreatedUtc, x.Id }).SingleOrDefaultAsync(ct);
            if (cursor != null)
            {
                actions = actions.Where(x => x.CreatedUtc < cursor.CreatedUtc
                                             || (x.CreatedUtc == cursor.CreatedUtc && x.Id.CompareTo(cursor.Id) < 0));
                merges = merges.Where(x => x.CreatedUtc < cursor.CreatedUtc
                                           || (x.CreatedUtc == cursor.CreatedUtc && x.Id.CompareTo(cursor.Id) < 0));
            }
        }

        // Из каждой таблицы — по странице, ленту собираем здесь: страница общей ленты
        // целиком лежит в объединении двух
        var fromActions = await actions
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(JournalPageSize)
            .Select(x => new AdminAccountJournalEntryDto
            {
                Id = x.Id,
                CreatedUtc = x.CreatedUtc,
                Kind = x.Kind,
                TargetUserId = x.TargetUserId,
                TargetUserName = x.TargetUserName,
                TargetExists = db.Profiles.Any(p => p.Id == x.TargetUserId),
                AdminUserId = x.AdminUserId,
                AdminName = db.Profiles.Where(p => p.Id == x.AdminUserId).Select(p => p.UserName).FirstOrDefault(),
                Reason = x.Reason
            })
            .ToListAsync(ct);

        var fromMerges = await merges
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(JournalPageSize)
            .Select(x => new AdminAccountJournalEntryDto
            {
                Id = x.Id,
                CreatedUtc = x.CreatedUtc,
                TargetUserId = x.RemovedUserId,
                TargetUserName = x.RemovedUserName,
                TargetExists = false,
                KeptUserId = x.KeptUserId,
                KeptUserName = x.KeptUserName,
                Platform = x.Platform
            })
            .ToListAsync(ct);

        // Порядок Id — как в базе: uuid в Postgres сравнивается побайтово, то есть так же,
        // как его строка; у Guid.CompareTo в .NET порядок другой
        var page = fromActions
            .Concat(fromMerges)
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id.ToString(), StringComparer.Ordinal)
            .Take(JournalPageSize)
            .ToArray();

        return new AdminAccountJournalPageDto
        {
            Entries = page,
            NextBefore = page.Length == JournalPageSize ? page[^1].Id : null
        };
    }

    /// <summary>
    /// Ссылка сброса пароля от поддержки (8.5): срок — <see cref="AccountOptions.AdminResetTokenTtl"/>.
    /// Аккаунта нет — <c>null</c>. Отказ — нет причины; у аккаунта роль в админке — как
    /// и удаление (раздел 10): иначе админ с ролью <c>Accounts</c> сбросом пароля зайдёт
    /// под админом с большими правами; у аккаунта нет пространств — такой удаляют, а не
    /// восстанавливают.
    ///
    /// Пока жива ссылка, выданная поддержкой, повторная выдача — новая взамен прежней (её
    /// текст в базе не хранится — только хеш) с прежним сроком: перевыдачей окно не продлить
    /// </summary>
    public async Task<AdminIssuedResetDto?> IssueReset(Guid userId, string? reason, Guid adminId, CancellationToken ct = default)
    {
        var why = RequireReason(reason);

        var userName = await db.Profiles.Where(x => x.Id == userId).Select(x => x.UserName).SingleOrDefaultAsync(ct);
        if (userName == null) return null;
        if (await db.UserInAdminRoles.AnyAsync(x => x.UserId == userId, ct))
            throw new BadRequestException(AuthMessages.AdminCannotBeDeleted);
        if (!await HasWorkspaces(userId, ct))
            throw new AdminRejectionException("У аккаунта нет пространств — восстанавливать нечего, такой аккаунт удаляют");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Строка пользователя заперта: из двух одновременных выдач вторая видит первую
            // и гасит её, а не расходится с ней двумя живыми ссылками
            await LockUser(userId, ct);
            var now = DateTime.UtcNow;
            var aliveUntil = await db.AccountTokens
                .Where(x => x.UserId == userId && x.Kind == AccountTokenKind.PasswordReset && x.IssuedByUserId != null
                            && x.UsedAtUtc == null && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
                .Select(x => (DateTime?)x.ExpiresAtUtc)
                .MaxAsync(ct);

            var expiresAtUtc = aliveUntil ?? now + account.Value.AdminResetTokenTtl;
            var raw = await tokens.IssueNow(userId, AccountTokenKind.PasswordReset, now, expiresAtUtc, adminId, ct);
            db.AccountAdminActions.Add(new AccountAdminAction
            {
                Id = Guid.CreateVersion7(),
                Kind = AccountAdminActionKind.IssueReset,
                AdminUserId = adminId,
                TargetUserId = userId,
                TargetUserName = userName,
                Reason = why,
                CreatedUtc = now
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new AdminIssuedResetDto
            {
                Url = $"{mail.Value.PublicBaseUrl.TrimEnd('/')}/auth/reset/{raw}",
                ExpiresAtUtc = expiresAtUtc,
                Replaced = aliveUntil != null
            };
        });
    }

    /// <summary>
    /// Освобождение почты (8.4, 8.5): почта уникальна среди всех аккаунтов и она же логин,
    /// поэтому чужой адрес можно вписать себе раньше владельца ящика. Владелец — аккаунт
    /// <paramref name="userId"/>; с аккаунта, где адрес лежит неподтверждённым, почта
    /// снимается вместе с живыми ссылками подтверждения. Подтверждённый адрес не
    /// снимается: подтвердил — значит, ящик его, и это не захват.
    ///
    /// Аккаунта нет — <c>null</c>. Отказ — нет причины или адреса, адрес никому не
    /// принадлежит или уже у этого аккаунта, подтверждён у другого. В журнал — две записи:
    /// у того, кто просил, и у того, с кого сняли
    /// </summary>
    public async Task<AdminReleasedContactDto?> ReleaseEmail(Guid userId, AdminReleaseEmailDto dto, Guid adminId, CancellationToken ct = default)
    {
        var why = RequireReason(dto.Reason);

        var email = dto.Email?.Trim() ?? string.Empty;
        if (email.Length == 0) throw new AdminRejectionException("Укажите адрес почты");

        var userName = await db.Profiles.Where(x => x.Id == userId).Select(x => x.UserName).SingleOrDefaultAsync(ct);
        if (userName == null) return null;

        // Регистр не различает тип колонки — citext; на нём же держится уникальный индекс (8.4)
        var holder = await db.Profiles.SingleOrDefaultAsync(x => x.Email == email, ct)
                     ?? throw new AdminRejectionException("Этот адрес не вписан ни в один аккаунт — освобождать нечего");
        if (holder.Id == userId)
            throw new AdminRejectionException("Этот адрес уже у этого аккаунта");
        if (holder.EmailConfirmed)
            throw new AdminRejectionException(
                $"У аккаунта {holder.UserName} адрес подтверждён — ящик его, а не захвачен; снять такой адрес нельзя");

        var released = holder.Email!;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await ReleaseEmail(holder.Id, released, ct);
            await tokens.Revoke(holder.Id, AccountTokenKind.EmailConfirm, ct);
            Journal(AccountAdminActionKind.ReleaseEmail, AccountAdminActionKind.EmailReleased, userId, userName, holder,
                why, $"адрес {released}", adminId);

            // Снятие, погашение ссылок и обе записи журнала — одной транзакцией
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return new AdminReleasedContactDto { ReleasedFromUserName = holder.UserName };
    }

    /// <summary>
    /// Снять адрес — только если он всё ещё тот же и не подтверждён: проверка выше читала его
    /// до записи, и владелец мог подтвердить адрес в этот миг. Условным <c>UPDATE</c>, а не
    /// трекером: из двух гонщиков победит один
    /// </summary>
    private async Task ReleaseEmail(Guid holderId, string email, CancellationToken ct)
    {
        var released = await db.Profiles
            .Where(x => x.Id == holderId && x.Email == email && !x.EmailConfirmed)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.Email, (string?)null)
                .SetProperty(p => p.EmailConfirmed, false), ct);
        if (released == 0)
            throw new AdminRejectionException("Адрес у этого аккаунта только что подтвердили или сменили — откройте карточку заново");
    }

    /// <summary>
    /// Освобождение телефона (спец. accounts, 6): телефон он же логин, а подтверждения
    /// у него нет — чужой номер вписывается раньше владельца, и тот свой уже не впишет.
    /// Владелец — аккаунт <paramref name="userId"/>; с аккаунта, где номер лежит, он
    /// снимается всегда: подтверждения, которое отличало бы владельца, у телефона нет.
    ///
    /// Аккаунта нет — <c>null</c>. Отказ — нет причины или номера, номер никому не принадлежит
    /// или уже у этого аккаунта. В журнал — две записи, как у почты.
    ///
    /// Уникального индекса у телефона нет (спец. accounts, 11) — занятость проверяют
    /// регистрация и профиль; держатель здесь — первый чужой аккаунт с этим номером
    /// </summary>
    public async Task<AdminReleasedContactDto?> ReleasePhone(Guid userId, AdminReleasePhoneDto dto, Guid adminId, CancellationToken ct = default)
    {
        var why = RequireReason(dto.Reason);

        var phone = dto.Phone?.Trim() ?? string.Empty;
        if (phone.Length == 0) throw new AdminRejectionException("Укажите телефон");

        var user = await db.Profiles.Where(x => x.Id == userId).Select(x => new { x.UserName, x.PhoneNumber }).SingleOrDefaultAsync(ct);
        if (user == null) return null;

        var holder = await db.Profiles
                         .Where(x => x.PhoneNumber == phone && x.Id != userId)
                         .OrderBy(x => x.Id)
                         .FirstOrDefaultAsync(ct)
                     ?? throw new AdminRejectionException(user.PhoneNumber == phone
                         ? "Этот телефон уже у этого аккаунта"
                         : "Этот телефон не вписан ни в один аккаунт — освобождать нечего");

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await ReleasePhone(holder.Id, phone, ct);
            Journal(AccountAdminActionKind.ReleasePhone, AccountAdminActionKind.PhoneReleased, userId, user.UserName, holder,
                why, $"телефон {phone}", adminId);

            // Снятие и обе записи журнала — одной транзакцией
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return new AdminReleasedContactDto { ReleasedFromUserName = holder.UserName };
    }

    /// <summary>
    /// Снять номер — только если он всё ещё тот же: владелец мог сменить его в этот миг.
    /// Условным <c>UPDATE</c>, как <see cref="ReleaseEmail(Guid, string, CancellationToken)"/>
    /// </summary>
    private async Task ReleasePhone(Guid holderId, string phone, CancellationToken ct)
    {
        var released = await db.Profiles
            .Where(x => x.Id == holderId && x.PhoneNumber == phone)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.PhoneNumber, (string?)null), ct);
        if (released == 0)
            throw new AdminRejectionException("Телефон у этого аккаунта только что сменили — откройте карточку заново");
    }

    /// <summary>
    /// Две записи освобождения: у того, кто просил (<paramref name="mine"/>), и у того, с кого
    /// сняли (<paramref name="theirs"/>) — иначе журнал аккаунта, лишившегося контакта, молчал бы
    /// </summary>
    private void Journal(AccountAdminActionKind mine, AccountAdminActionKind theirs, Guid userId, string userName,
        Profile holder, string why, string released, Guid adminId)
    {
        var now = DateTime.UtcNow;
        db.AccountAdminActions.Add(new AccountAdminAction
        {
            Id = Guid.CreateVersion7(),
            Kind = mine,
            AdminUserId = adminId,
            TargetUserId = userId,
            TargetUserName = userName,
            Reason = WithNote(why, $"{released} снят с аккаунта {holder.UserName}"),
            CreatedUtc = now
        });
        db.AccountAdminActions.Add(new AccountAdminAction
        {
            Id = Guid.CreateVersion7(),
            Kind = theirs,
            AdminUserId = adminId,
            TargetUserId = holder.Id,
            TargetUserName = holder.UserName,
            Reason = WithNote(why, $"{released} освобождён для аккаунта {userName}"),
            CreatedUtc = now
        });
    }

    /// <summary>
    /// Удалить аккаунт без пространств (8.5): восстанавливать в нём нечего — человек
    /// регистрируется заново, логин освобождается. С причиной в журнале, общим путём
    /// удаления (раздел 10). У аккаунта есть пространства — отказ: такой восстанавливают
    /// ссылкой. Аккаунта нет — <c>false</c>
    /// </summary>
    public async Task<bool> Delete(Guid userId, string? reason, Guid adminId, CancellationToken ct = default)
    {
        var why = RequireReason(reason);

        var userName = await db.Profiles.Where(x => x.Id == userId).Select(x => x.UserName).SingleOrDefaultAsync(ct);
        if (userName == null) return false;

        // Проверка — в транзакции удаления, под её замками: пространство, созданное между
        // проверкой и удалением, иначе стёрлось бы вместе с аккаунтом
        await deletion.Delete(userId, ct, precondition: async token =>
        {
            if (await HasWorkspaces(userId, token))
                throw new AdminRejectionException("У аккаунта есть пространства — его восстанавливают ссылкой сброса, а не удаляют");
        }, journal: context => context.AccountAdminActions.Add(new AccountAdminAction
        {
            Id = Guid.CreateVersion7(),
            Kind = AccountAdminActionKind.Delete,
            AdminUserId = adminId,
            TargetUserId = userId,
            TargetUserName = userName,
            Reason = why,
            CreatedUtc = DateTime.UtcNow
        }));

        return true;
    }

    /// <summary>Причина обязательна у всякой работы с чужим аккаунтом (8.5)</summary>
    public static string RequireReason(string? reason)
    {
        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why)) throw new AdminRejectionException("Укажите причину");
        if (why.Length > AccountAdminAction.ReasonMaxLength)
            throw new AdminRejectionException($"Причина — не длиннее {AccountAdminAction.ReasonMaxLength} символов");
        return why;
    }

    /// <summary>
    /// Пространства аккаунта — как в карточке (<see cref="AccountCards"/>): хоть одно право
    /// в каком-нибудь пространстве. Мягкого удаления у пространства нет — права и есть членство
    /// </summary>
    private Task<bool> HasWorkspaces(Guid userId, CancellationToken ct) =>
        db.UserInPermissions.AnyAsync(x => x.UserId == userId, ct);

    /// <summary>Запереть строку пользователя до конца транзакции</summary>
    private async Task LockUser(Guid userId, CancellationToken ct) =>
        await db.Users
            .FromSql($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(ct);

    /// <summary>Причина с пометкой операции — в пределах длины колонки журнала</summary>
    private static string WithNote(string reason, string note)
    {
        // Пометка короче колонки всегда: адрес — до 255, логин — до 20
        var suffix = $" · {note}";
        var room = Math.Max(0, AccountAdminAction.ReasonMaxLength - suffix.Length);
        return (reason.Length > room ? reason[..room] : reason) + suffix;
    }

    /// <summary>
    /// Шаблон <c>ILIKE</c> «содержит»: <c>%</c>, <c>_</c> и сама косая черта в искомом —
    /// буквально, а не подстановками
    /// </summary>
    private static string Like(string text) =>
        "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private const string Escape = "\\";
}
