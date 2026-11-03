using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Services;

/// <summary>
/// Восстановление пароля по ссылке (спец. accounts, 8.2 и 8.4): письмо со ссылкой
/// и сброс по ней. Ссылка от поддержки (8.5) проверяется здесь же — у неё свой срок,
/// он лежит в самом токене
/// </summary>
public class PasswordReset(
    ApplicationContext db,
    AccountTokens tokens,
    EmailConfirmation emails,
    SessionGuard sessions,
    IOptions<AccountOptions> options)
{
    /// <summary>
    /// Письмо со ссылкой сброса — по почте, только аккаунту, у которого она подтверждена,
    /// и в пределах лимита. Ищем только по почте: письмо уходит на неё же, логин и телефон
    /// тут ничего не дают. Что бы ни вышло, вызывающий отвечает одно и то же: форма
    /// не должна рассказывать, есть ли такой аккаунт
    /// </summary>
    public async Task Request(string email, CancellationToken ct)
    {
        email = email.Trim();

        // Почта — `citext`: регистр в адресе не мешает найти аккаунт
        var account = await db.Profiles
            .Where(x => x.Email == email)
            .Select(x => new { x.Id, x.Email, x.EmailConfirmed })
            .SingleOrDefaultAsync(ct);
        if (account == null || !account.EmailConfirmed || account.Email == null) return;

        if (await emails.OverLimit(account.Id, AccountTokenKind.PasswordReset, ct)) return;

        await tokens.IssueForMail(account.Id, AccountTokenKind.PasswordReset, options.Value.ResetTokenTtl, account.Email, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Новый пароль по ссылке: токен гаснет вместе с остальными живыми ссылками сброса
    /// (из писем и от поддержки, 8.4), все сессии завершаются (8.1). Ответ — чей аккаунт:
    /// вызывающий выдаёт новую сессию, вход без повторного ввода (8.2)
    /// </summary>
    public async Task<Guid> Reset(string rawToken, string newPassword, CancellationToken ct)
    {
        var token = await tokens.Consume(AccountTokenKind.PasswordReset, rawToken, ct)
                    ?? throw new BadRequestException(AuthMessages.LinkInvalid);
        var user = await db.Users.SingleAsync(x => x.Id == token.UserId, ct);

        PasswordHasher.Set(user, newPassword);
        SessionGuard.Revoke(user, DateTime.UtcNow);
        await tokens.Revoke(user.Id, AccountTokenKind.PasswordReset, ct);
        await db.SaveChangesAsync(ct);
        sessions.Forget(user.Id);

        return user.Id;
    }
}
