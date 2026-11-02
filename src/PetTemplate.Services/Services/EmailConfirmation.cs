using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Mail;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Services;

/// <summary>
/// Подтверждение почты (спец. accounts, 8.4). Восстанавливать доступ на неподтверждённый
/// адрес — значит отдать аккаунт любому, кто впишет туда свою почту, поэтому письмо
/// сброса уходит только на подтверждённую.
///
/// <list type="bullet">
/// <item>новая почта — на регистрации или сменой — письмо со ссылкой на сутки;</item>
/// <item>смена сбрасывает подтверждение, а прежний подтверждённый адрес получает «Почту
/// сменили» — тихая смена чужим не остаётся незамеченной;</item>
/// <item>почта принадлежит одному аккаунту, подтверждённая или нет.</item>
/// </list>
/// </summary>
public class EmailConfirmation(ApplicationContext db, AccountTokens tokens, IOptions<AccountOptions> options)
{
    /// <summary>
    /// Писем в 2 минуты и в сутки на аккаунт (8.4) — у подтверждения и у сброса счёт свой:
    /// сброс просит кто угодно, знающий адрес, и общий счёт отдал бы чужим запросам
    /// «Отправить ещё раз» владельца
    /// </summary>
    public const int PerTwoMinutes = 1;
    public const int PerDay = 5;

    /// <summary>
    /// Письмо подтверждения на адрес профиля — в трекер вместе с токеном (outbox).
    /// Сохраняет вызывающий. Сверх лимита (8.4) письма нет — <c>false</c>: регистрация
    /// всё равно заводит аккаунт, а письмо человек попросит позже «Отправить ещё раз»
    /// </summary>
    public async Task<bool> Request(Guid userId, string email, CancellationToken ct)
    {
        if (await OverLimit(userId, AccountTokenKind.EmailConfirm, ct)) return false;

        await Issue(userId, email, ct);
        return true;
    }

    /// <summary>«Отправить ещё раз» (8.4) — не чаще лимита</summary>
    public async Task Resend(Guid userId, CancellationToken ct)
    {
        var email = await db.Profiles.Where(x => x.Id == userId).Select(x => x.Email).SingleAsync(ct);
        if (string.IsNullOrWhiteSpace(email)) throw new BadRequestException(AuthMessages.NoEmail);
        if (await OverLimit(userId, AccountTokenKind.EmailConfirm, ct)) throw new BadRequestException(AuthMessages.TooManyEmails);

        await Issue(userId, email, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Сменить почту — частью сохранения профиля (<c>Account/Save</c>): подтверждение
    /// сбрасывается, на новый адрес — письмо, прежнему подтверждённому — «Почту сменили».
    /// Пустой адрес — почту убрать. Писем подтверждения сверх лимита (8.4) — <c>400</c>,
    /// тот же отказ, что у «Отправить ещё раз»: иначе смена почты туда-сюда обходила бы
    /// лимит. Живые ссылки подтверждения прежнего адреса и сброса пароля гаснут: сброс
    /// выписан на прежний подтверждённый адрес, и письмо по нему уйти уже не должно.
    /// Ничего не сохраняет: изменения уходят общим <c>SaveChanges</c> профиля вместе
    /// с письмами (outbox)
    /// </summary>
    public async Task Change(Profile profile, string? email, CancellationToken ct)
    {
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (string.Equals(profile.Email, email, StringComparison.OrdinalIgnoreCase)) return;

        // Почта — у одного аккаунта, подтверждённая или нет (8.4): она же логин. Чужой
        // вписанный адрес освобождает поддержка (8.5). `citext`: регистр не различается
        if (email != null && await db.Profiles.AnyAsync(x => x.Email == email && x.Id != profile.Id, ct))
        {
            throw new BadRequestException(AuthMessages.EmailTaken);
        }

        if (email != null && await OverLimit(profile.Id, AccountTokenKind.EmailConfirm, ct))
        {
            throw new BadRequestException(AuthMessages.TooManyEmails);
        }

        // Письма — только на подтверждённые адреса: неподтверждённый мог быть вписан
        // с опечаткой или чужой. Исключение одно — само письмо подтверждения (спец. mail, 3)
        if (profile.EmailConfirmed && profile.Email != null)
        {
            db.Enqueue(new SendMailCommand { Kind = MailKind.EmailChanged, To = profile.Email, Language = profile.Language });
        }

        profile.Email = email;
        profile.EmailConfirmed = false;

        await tokens.Revoke(profile.Id, AccountTokenKind.PasswordReset, ct);
        await tokens.Revoke(profile.Id, AccountTokenKind.EmailConfirm, ct);
        if (email != null) await Issue(profile.Id, email, ct);
    }

    /// <summary>
    /// Переход по ссылке. Токен не живой, адрес в профиле с тех пор сменили — ссылка
    /// недействительна. Адрес у другого аккаунта быть не может — почта уникальна (8.4)
    /// </summary>
    public async Task Confirm(string rawToken, CancellationToken ct)
    {
        var token = await tokens.Consume(AccountTokenKind.EmailConfirm, rawToken, ct)
                    ?? throw new BadRequestException(AuthMessages.LinkInvalid);
        var profile = await db.Profiles.SingleAsync(x => x.Id == token.UserId, ct);

        if (!string.Equals(profile.Email, token.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException(AuthMessages.LinkInvalid);
        }

        profile.EmailConfirmed = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Лимит писем вида на аккаунт (8.4): считается по выпущенным письмом токенам — они
    /// и есть письма. Ссылки поддержки (<see cref="AccountToken.IssuedByUserId"/>) писем
    /// не шлют и не считаются
    /// </summary>
    public async Task<bool> OverLimit(Guid userId, AccountTokenKind kind, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var dayAgo = now.AddDays(-1);
        var recent = await db.AccountTokens
            .Where(x => x.UserId == userId && x.Kind == kind && x.IssuedByUserId == null && x.CreatedAtUtc > dayAgo)
            .Select(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var twoMinutesAgo = now.AddMinutes(-2);
        return recent.Count >= PerDay || recent.Count(x => x > twoMinutesAgo) >= PerTwoMinutes;
    }

    private Task Issue(Guid userId, string email, CancellationToken ct) =>
        tokens.IssueForMail(userId, AccountTokenKind.EmailConfirm, options.Value.EmailConfirmTokenTtl, email.Trim(), ct);
}
