using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Mail;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Services;

/// <summary>
/// Одноразовые токены аккаунта (спец. accounts, 8.2 и 8.4): 32 случайных байта, в базе —
/// только хеш. Новое подтверждение почты гасит прежнее; сброс или подтверждение гасит
/// использованный.
///
/// Новый запрос сброса прежние ссылки сброса <b>не</b> гасит (8.4): сброс просит кто
/// угодно, знающий адрес, и чужие запросы гасили бы ссылку, которую владелец уже открыл.
/// Все живые ссылки сброса гасит сам сброс (<see cref="PasswordReset.Reset"/>)
/// </summary>
public class AccountTokens(ApplicationContext db)
{
    /// <summary>
    /// Токен для письма: строка без хеша и команда отправки — в трекер, сохраняет вызывающий
    /// своим <c>SaveChanges</c> (outbox, спец. mail, 4). Сам токен придумает обработчик очереди
    /// </summary>
    public async Task<AccountToken> IssueForMail(Guid userId, AccountTokenKind kind, TimeSpan ttl, string? email, CancellationToken ct)
    {
        if (kind == AccountTokenKind.EmailConfirm)
        {
            // Подтверждается последний вписанный адрес — ссылки на прежние не нужны
            await Revoke(userId, kind, ct);
        }

        var now = DateTime.UtcNow;
        var token = new AccountToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Kind = kind,
            Email = email,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + ttl
        };
        db.AccountTokens.Add(token);

        db.Enqueue(new SendMailCommand
        {
            Kind = kind == AccountTokenKind.PasswordReset ? MailKind.PasswordReset : MailKind.EmailConfirm,
            TokenId = token.Id
        });

        return token;
    }

    /// <summary>
    /// Токен, который отдаётся человеку сразу (ссылка сброса от поддержки, 8.5): прежние живые
    /// того же вида гаснут, строка с хешем — в трекер, сам токен — ответом. Сохраняет вызывающий.
    ///
    /// Время выпуска и срок — от вызывающего, а не своё <c>UtcNow</c>: перевыдача поддержкой
    /// держит прежний срок (8.5), и обе отметки должны считаться от одного «сейчас»
    /// </summary>
    public async Task<string> IssueNow(Guid userId, AccountTokenKind kind, DateTime nowUtc, DateTime expiresAtUtc,
        Guid issuedBy, CancellationToken ct)
    {
        await Revoke(userId, kind, ct);

        var raw = NewRaw();
        db.AccountTokens.Add(new AccountToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Kind = kind,
            Hash = HashOf(raw),
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            IssuedByUserId = issuedBy
        });

        return raw;
    }

    /// <summary>
    /// Найти живой токен по присланному и израсходовать: хеш совпал, не использован,
    /// не погашен, срок не вышел. Иначе <c>null</c>.
    ///
    /// Расход — условным <c>UPDATE … WHERE used_at_utc IS NULL</c> сразу в базу, а не
    /// трекером вызывающего: из двух одновременных переходов по одной ссылке строку
    /// поменяет только один, второй получит <c>null</c> (8.4). Упало дальнейшее сохранение
    /// вызывающего — ссылка всё равно израсходована: человек попросит новую. Отдаётся
    /// без отслеживания — менять в нём нечего
    /// </summary>
    public async Task<AccountToken?> Consume(AccountTokenKind kind, string raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var hash = HashOf(raw);
        var now = DateTime.UtcNow;
        var token = await db.AccountTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Hash == hash && x.Kind == kind, ct);
        if (token == null || token.UsedAtUtc != null || token.RevokedAtUtc != null || token.ExpiresAtUtc <= now)
        {
            return null;
        }

        var used = await db.AccountTokens
            .Where(x => x.Id == token.Id && x.UsedAtUtc == null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.UsedAtUtc, now), ct);
        if (used == 0) return null;

        token.UsedAtUtc = now;
        return token;
    }

    /// <summary>
    /// Погасить живые токены вида — в трекере, сохраняет вызывающий. Смена почты гасит
    /// подтверждение прежнего адреса и ссылки сброса, выписанные на него (8.4); сброс
    /// пароля — остальные ссылки сброса
    /// </summary>
    public async Task Revoke(Guid userId, AccountTokenKind kind, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var active = await db.AccountTokens
            .Where(x => x.UserId == userId && x.Kind == kind && x.UsedAtUtc == null && x.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in active)
        {
            token.RevokedAtUtc = now;
        }
    }

    /// <summary>Новый случайный токен — 32 байта в base64url: так он уходит в ссылку</summary>
    public static string NewRaw()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string HashOf(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
