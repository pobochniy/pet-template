using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Services;

/// <summary>
/// Аккаунт вошедшего человека: логин, пароль, контакты, входы площадок, удаление (спец. accounts, разделы 3, 4, 6, 8.1, 9).
/// Отдельно от <see cref="AuthService"/>: тот впускает анонимного, а здесь всё делается
/// над своим аккаунтом по действующей сессии
/// </summary>
public class AccountService(
    ApplicationContext db,
    SessionGuard sessions,
    EmailConfirmation emails,
    AccountDeletion deletion,
    IOptions<AccountOptions> options)
{
    /// <summary>
    /// Смена логина (4.3). Занятый — <c>400</c>, а не подмена. Старый логин освобождается
    /// сразу: всё в PetTemplate ссылается на человека по id, а не по логину.
    ///
    /// Свой же логин в другом регистре — законная смена (<c>masha</c> → <c>Masha</c>),
    /// занятым он не считается. Сравнение без учёта регистра даёт <c>citext</c>
    /// </summary>
    public async Task ChangeUserName(Guid userId, string userName, CancellationToken ct = default)
    {
        userName = userName.Trim();

        if (await db.Profiles.AnyAsync(x => x.UserName == userName && x.Id != userId, ct))
        {
            throw new BadRequestException(AuthMessages.UserNameTaken);
        }

        var profile = await db.Profiles.SingleAsync(x => x.Id == userId, ct);
        profile.UserName = userName;
        profile.UserNameIsGenerated = false;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsDuplicate(ex, DbErrors.UserNameIndex))
        {
            // Заняли между проверкой и записью — тот же отказ, а не 500
            throw new BadRequestException(AuthMessages.UserNameTaken);
        }
    }

    /// <summary>
    /// Задать пароль (8.1): аккаунту, у которого его нет. С паролем — <c>400</c>, для этого
    /// есть <see cref="ChangePassword"/>; исключение — свежий вход через Telegram или VK (8.3),
    /// тогда <paramref name="freshVia"/> — его площадка: так восстанавливают забытый пароль
    /// через привязанный вход. Свежесть знает только токен — её передаёт контроллер. Вход,
    /// привязанный только что, пароль так не заменяет — <c>400 auth.error.loginRecentlyLinked</c>
    /// (<see cref="IsSettled"/>).
    ///
    /// Завершает остальные сессии: текущая получит новый токен ответом
    /// </summary>
    public async Task SetPassword(Guid userId, string newPassword, ExternalProvider? freshVia = null, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);

        if (user.PasswordHash != null)
        {
            if (freshVia is not { } provider) throw new BadRequestException(AuthMessages.PasswordAlreadySet);
            if (!IsSettled(user, provider)) throw new BadRequestException(AuthMessages.LoginRecentlyLinked);
        }

        await ReplacePassword(user, newPassword, ct);
    }

    /// <summary>
    /// Задаётся ли сейчас пароль без текущего (8.3): сессия открыта свежим входом площадки
    /// <paramref name="freshVia"/>, и этот вход привязан не только что. Тот же вход
    /// подтверждает и владение аккаунтом (8.6)
    /// </summary>
    public async Task<bool> CanSetPasswordWithoutCurrent(Guid userId, ExternalProvider? freshVia, CancellationToken ct = default)
    {
        if (freshVia is not { } provider) return false;

        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
        return IsSettled(user, provider);
    }

    /// <summary>
    /// Сменить пароль (8.1): текущий и новый. Неверный текущий — <c>false</c>, ничего
    /// не меняется; отказ по полю отдаёт контроллер (<c>422</c>).
    /// Завершает остальные сессии
    /// </summary>
    public async Task<bool> ChangePassword(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);

        if (!PasswordHasher.Verify(user, currentPassword)) return false;

        await ReplacePassword(user, newPassword, ct);
        return true;
    }

    /// <summary>
    /// Пароль этого аккаунта (8.6) — подтвердить владение перед привязкой, отвязкой, сменой
    /// почты и слиянием. Нет пароля — <c>false</c>
    /// </summary>
    public async Task<bool> VerifyPassword(Guid userId, string password, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
        return PasswordHasher.Verify(user, password);
    }

    /// <summary>
    /// Меняет ли <c>Account/Save</c> с этой почтой сохранённую (8.6): смена почты требует
    /// свежего входа, а телефон и комментарий — нет. Сравнение — как у <see cref="EmailConfirmation.Change"/>
    /// </summary>
    public async Task<bool> EmailChanges(Guid userId, string? email, CancellationToken ct = default)
    {
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        var saved = await db.Profiles.Where(x => x.Id == userId).Select(x => x.Email).SingleAsync(ct);
        return !string.Equals(saved, email, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Блок «Основное» (раздел 6). Свежесть входа знает только токен — её ставит контроллер</summary>
    public async Task<AccountLoginsDto> Logins(Guid userId, CancellationToken ct = default)
    {
        return await db.Profiles
            .Where(x => x.Id == userId)
            .Select(x => new AccountLoginsDto
            {
                UserName = x.UserName,
                UserNameIsGenerated = x.UserNameIsGenerated,
                HasPassword = x.User!.PasswordHash != null,
                Email = x.Email,
                EmailConfirmed = x.EmailConfirmed,
                Phone = x.PhoneNumber,
                Comment = x.Comment,
                TelegramLinked = x.User.TelegramId != null,
                VkLinked = x.User.VkId != null
            })
            .SingleAsync(ct);
    }

    /// <summary>
    /// Почта, телефон, комментарий и язык (раздел 6). Почта и телефон — логины для входа,
    /// поэтому у одного аккаунта: занятые — <c>400</c>. Пустое — «убрать»; язык без значения
    /// не меняется.
    ///
    /// Смена почты (8.4) — <see cref="EmailConfirmation.Change"/>: подтверждение сбрасывается,
    /// новый адрес получает письмо, прежний подтверждённый — «Почту сменили», живые ссылки
    /// гаснут. Письма ставятся в очередь тем же сохранением, что и профиль (outbox).
    ///
    /// У почты уникальный индекс — гонку ловит он. У телефона индекса нет (спец. accounts, 11):
    /// занятость проверяется здесь, как при регистрации
    /// </summary>
    public async Task<AccountLoginsDto> Save(Guid userId, AccountSaveDto dto, CancellationToken ct = default)
    {
        var phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        var comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment;

        if (phone != null && await db.Profiles.AnyAsync(x => x.PhoneNumber == phone && x.Id != userId, ct))
        {
            throw new BadRequestException(AuthMessages.PhoneTaken);
        }

        var profile = await db.Profiles.SingleAsync(x => x.Id == userId, ct);
        await emails.Change(profile, dto.Email, ct);

        profile.PhoneNumber = phone;
        profile.Comment = comment;
        if (!string.IsNullOrEmpty(dto.Language))
        {
            profile.Language = dto.Language.ToLowerInvariant();
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsDuplicate(ex, DbErrors.EmailIndex))
        {
            throw new BadRequestException(AuthMessages.EmailTaken);
        }

        return await Logins(userId, ct);
    }

    /// <summary>
    /// Язык из переключателя в шапке (спец. accounts, 8.4). Отдельно от <see cref="Save"/>:
    /// та принимает почту, телефон и комментарий целиком (пустое — «убрать»), и
    /// переключателю, у которого формы профиля нет, пришлось бы слать устаревающую копию.
    /// Код заведомо из белого списка — незнакомый отбила валидация
    /// </summary>
    public async Task SetLanguage(Guid userId, string language, CancellationToken ct = default)
    {
        var profile = await db.Profiles.SingleAsync(x => x.Id == userId, ct);
        profile.Language = language.ToLowerInvariant();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Привязать вход площадки (раздел 6). Тот же — ничего не делает. Другой уже привязан —
    /// <c>400 auth.error.loginAlreadyLinked</c>: один на аккаунт (раздел 3). Вход принадлежит
    /// другому аккаунту — привязки нет, ответ — id того аккаунта: вызывающий ведёт в слияние
    /// (раздел 7). Привязано — <c>null</c>.
    ///
    /// Время привязки — <c>TelegramLinkedAtUtc</c>/<c>VkLinkedAtUtc</c>: сутки свежий вход
    /// этой площадкой не задаёт пароль без текущего и не подтверждает владение (8.3, 8.6)
    /// </summary>
    public async Task<Guid?> Link(Guid userId, ExternalProvider provider, long externalId, CancellationToken ct = default)
    {
        var owner = await OwnerOf(provider, externalId, ct);

        if (owner == userId) return null;
        if (owner != null) return owner;

        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        if (Slot(user, provider) != null) throw new BadRequestException(AuthMessages.LoginAlreadyLinked);

        if (provider == ExternalProvider.Telegram)
        {
            user.TelegramId = externalId;
            user.TelegramLinkedAtUtc = DateTime.UtcNow;
        }
        else
        {
            user.VkId = externalId;
            user.VkLinkedAtUtc = DateTime.UtcNow;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsDuplicate(ex,
                                               provider == ExternalProvider.Telegram ? DbErrors.TelegramIdIndex : DbErrors.VkIdIndex))
        {
            // Тот же вход одновременно привязали к другому аккаунту (или завели им новый) —
            // между проверкой и записью. Отказ, а не 500 и не слияние: владелец только что
            // сменился, и человек повторит привязку — тогда уже со слиянием
            throw new BadRequestException(AuthMessages.LoginTaken);
        }

        return null;
    }

    /// <summary>
    /// Отвязать вход площадки (раздел 6). Не привязан — ничего не делает. Последний способ
    /// войти — <c>400 auth.error.lastLogin</c> (раздел 3): аккаунт стал бы недостижим.
    /// Логин <c>tg&lt;id&gt;</c> остаётся аккаунту (4.2)
    /// </summary>
    public async Task Unlink(Guid userId, ExternalProvider provider, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        if (Slot(user, provider) == null) return;

        var remaining = (user.PasswordHash != null ? 1 : 0)
                        + (provider != ExternalProvider.Telegram && user.TelegramId != null ? 1 : 0)
                        + (provider != ExternalProvider.Vk && user.VkId != null ? 1 : 0);
        if (remaining == 0) throw new BadRequestException(AuthMessages.LastLogin);

        if (provider == ExternalProvider.Telegram)
        {
            user.TelegramId = null;
            user.TelegramLinkedAtUtc = null;
        }
        else
        {
            user.VkId = null;
            user.VkLinkedAtUtc = null;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Удалить свой аккаунт (раздел 9) — физически и сразу (<see cref="AccountDeletion"/>).
    /// Подтверждение — свой логин, без учёта регистра (<c>citext</c>, 4.1): не он —
    /// <c>400 auth.error.deleteConfirmMismatch</c>
    /// </summary>
    public async Task Delete(Guid userId, string confirmUserName, CancellationToken ct = default)
    {
        var userName = confirmUserName.Trim();
        if (!await db.Profiles.AnyAsync(x => x.Id == userId && x.UserName == userName, ct))
        {
            throw new BadRequestException(AuthMessages.DeleteConfirmMismatch);
        }

        await deletion.Delete(userId, ct);
    }

    private Task<Guid?> OwnerOf(ExternalProvider provider, long externalId, CancellationToken ct) =>
        db.Users
            .Where(x => provider == ExternalProvider.Telegram ? x.TelegramId == externalId : x.VkId == externalId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);

    private static long? Slot(User user, ExternalProvider provider) =>
        provider == ExternalProvider.Telegram ? user.TelegramId : user.VkId;

    /// <summary>
    /// Вход площадки привязан и привязан не только что (8.3): только такой свежим входом
    /// задаёт пароль без текущего. Иначе украденная сессия привязала бы свой Telegram, вошла
    /// им и сменила пароль, не зная его
    /// </summary>
    private bool IsSettled(User user, ExternalProvider provider)
    {
        if (Slot(user, provider) == null) return false;

        var linkedAt = provider == ExternalProvider.Telegram ? user.TelegramLinkedAtUtc : user.VkLinkedAtUtc;
        return linkedAt == null || DateTime.UtcNow - linkedAt.Value >= options.Value.RecentLinkWindow;
    }

    /// <summary>
    /// Новый пароль и отзыв выданных токенов — одним сохранением: кто-то знал старый
    /// пароль, и его сессия не должна пережить смену (8.1)
    /// </summary>
    private async Task ReplacePassword(User user, string newPassword, CancellationToken ct)
    {
        PasswordHasher.Set(user, newPassword);
        SessionGuard.Revoke(user, DateTime.UtcNow);

        await db.SaveChangesAsync(ct);
        sessions.Forget(user.Id);
    }
}
