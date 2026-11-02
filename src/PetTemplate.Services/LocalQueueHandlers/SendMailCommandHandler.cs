using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.Mail;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Отправка письма из очереди (спец. mail, 4). Провайдер упал — исключение,
/// и очередь повторит; письмо не теряется.
///
/// Идемпотентность — по токену: письмо уже ушло, токен использован, погашен или
/// просрочен — второе письмо не нужно. Сам токен придумывается здесь, перед отправкой:
/// в базе остаётся только хеш. Упало сохранение после отправки — повтор пришлёт новую
/// ссылку, а первая не сработает: хеш её так и не лёг в базу
/// </summary>
public class SendMailCommandHandler(
    ApplicationContext db,
    IMailSender sender,
    MailMetrics metrics,
    IOptions<MailOptions> options) : ICommandHandler<SendMailCommand>
{
    public async Task Handle(SendMailCommand command, CancellationToken ct)
    {
        var message = command.TokenId is { } tokenId
            ? await TokenMail(command.Kind, tokenId, ct)
            : MailTemplates.Render(command.Kind, command.Language, command.To!, $"{BaseUrl}/auth/recover");

        if (message == null) return;

        try
        {
            await sender.Send(message, ct);
        }
        catch
        {
            metrics.Failed(command.Kind);
            throw;
        }

        await db.SaveChangesAsync(ct);
        metrics.Sent(command.Kind);
    }

    private string BaseUrl => options.Value.PublicBaseUrl.TrimEnd('/');

    private async Task<MailMessage?> TokenMail(MailKind kind, Guid tokenId, CancellationToken ct)
    {
        var token = await db.AccountTokens.SingleOrDefaultAsync(x => x.Id == tokenId, ct);
        if (token == null || token.MailSentAtUtc != null || token.UsedAtUtc != null
            || token.RevokedAtUtc != null || token.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return null;
        }

        // Адрес — тот, на который выписан токен, а не нынешний в профиле: сброс уходит
        // только на подтверждённый (спец. accounts, 8.4), а почту могли сменить между
        // запросом и отправкой. Смена гасит живые ссылки сброса, так что сюда такое
        // доходит разве что гонкой
        var to = token.Email;
        if (string.IsNullOrWhiteSpace(to)) return null;

        var language = await db.Profiles
            .Where(x => x.Id == token.UserId)
            .Select(x => x.Language)
            .SingleAsync(ct);

        var raw = AccountTokens.NewRaw();
        token.Hash = AccountTokens.HashOf(raw);
        token.MailSentAtUtc = DateTime.UtcNow;

        var path = token.Kind == AccountTokenKind.PasswordReset ? "reset" : "confirm-email";
        // Round, а не Ceiling: отметки могут разойтись на доли секунды (два вызова UtcNow, микросекунды в Postgres),
        // и Ceiling превращал 24 часа в 25
        var hours = (int)Math.Round((token.ExpiresAtUtc - token.CreatedAtUtc).TotalHours);
        return MailTemplates.Render(kind, language, to, $"{BaseUrl}/auth/{path}/{raw}", hours);
    }
}
