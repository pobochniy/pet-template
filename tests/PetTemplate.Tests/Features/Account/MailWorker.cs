using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Mail;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Account;

/// <summary>
/// Отправка письма воркером очереди (спец. mail, 4): токен придумывается при отправке,
/// в базе остаётся только хеш, повтор второе письмо не шлёт, упавший провайдер — повтор,
/// а не потеря
/// </summary>
public class MailWorker
{
    private const string OlegEmail = "oleg@roga-i-kopyta.ru";
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(15);

    private readonly FakeMailSender _mail = new();

    private static PetTemplate.Db.Entity.Profile Oleg(string email = OlegEmail)
    {
        var (_, oleg) = new UserBuilder("Oleg").WithContacts(email, "+79990000001").Please();
        return oleg;
    }

    private static AccountToken Token(Guid userId, AccountTokenKind kind, string email, TimeSpan life)
    {
        var now = DateTime.UtcNow;
        return new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Kind = kind,
            Email = email,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + life
        };
    }

    private static Db.Entity.LocalQueue Command(SendMailCommand command) => new()
    {
        Id = Guid.CreateVersion7(),
        Data = JsonSerializer.Serialize(command),
        CreatedAtUtc = DateTime.UtcNow,
        CommandType = CommandQueue.TypeOf<SendMailCommand>()
    };

    [Fact]
    public async Task SendsLinkAndKeepsOnlyHash()
    {
        var oleg = Oleg();
        var token = Token(oleg.Id, AccountTokenKind.EmailConfirm, OlegEmail, TimeSpan.FromDays(1));
        var command = Command(new SendMailCommand { Kind = MailKind.EmailConfirm, TokenId = token.Id });

        var db = await Given.WorkerClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(token);
            x.LocalQueue.Add(command);
        }, _mail.Services());

        await Processed(db, command);

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal(OlegEmail, mail.To);
        Assert.Contains("24", mail.Text);
        var stored = await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == token.Id);
        Assert.NotNull(stored.MailSentAtUtc);
        var raw = mail.Text.Split("/auth/confirm-email/")[1].Split('\n')[0].Trim();
        Assert.Equal(AccountTokens.HashOf(raw), stored.Hash);
    }

    /// <summary>Письмо — на языке профиля (спец. mail, 3)</summary>
    [Fact]
    public async Task WritesInProfileLanguage()
    {
        var oleg = Oleg();
        oleg.Language = "en";
        var token = Token(oleg.Id, AccountTokenKind.PasswordReset, OlegEmail, TimeSpan.FromHours(1));
        var command = Command(new SendMailCommand { Kind = MailKind.PasswordReset, TokenId = token.Id });

        var db = await Given.WorkerClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(token);
            x.LocalQueue.Add(command);
        }, _mail.Services());

        await Processed(db, command);

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal("Reset your PetTemplate password", mail.Subject);
        Assert.Contains("https://example.com/auth/reset/", mail.Text);
    }

    /// <summary>Повтор команды по токену, письмо которого уже ушло, второго письма не шлёт</summary>
    [Fact]
    public async Task DoesNotSendTwice()
    {
        var oleg = Oleg();
        var token = Token(oleg.Id, AccountTokenKind.EmailConfirm, OlegEmail, TimeSpan.FromDays(1));
        token.MailSentAtUtc = DateTime.UtcNow;
        token.Hash = "ALREADY";
        var command = Command(new SendMailCommand { Kind = MailKind.EmailConfirm, TokenId = token.Id });

        var db = await Given.WorkerClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(token);
            x.LocalQueue.Add(command);
        }, _mail.Services());

        await Processed(db, command);

        Assert.Empty(_mail.Sent);
    }

    /// <summary>
    /// Сброс запросили на подтверждённый адрес, а до отправки почту в профиле сменили:
    /// письмо уходит на адрес из токена, а не на новый, ещё не подтверждённый (8.4)
    /// </summary>
    [Fact]
    public async Task ResetGoesToAddressFromTokenNotToChangedProfile()
    {
        var oleg = Oleg("chuzhoi@shariki-i-palochki.ru");
        var token = Token(oleg.Id, AccountTokenKind.PasswordReset, OlegEmail, TimeSpan.FromHours(1));
        var command = Command(new SendMailCommand { Kind = MailKind.PasswordReset, TokenId = token.Id });

        var db = await Given.WorkerClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(token);
            x.LocalQueue.Add(command);
        }, _mail.Services());

        await Processed(db, command);

        Assert.Equal(OlegEmail, Assert.Single(_mail.Sent).To);
    }

    /// <summary>
    /// Сброс запросили, а до отправки почту сменили: смена погасила ссылку, и письмо
    /// не уходит вовсе — ни на новый адрес, ни на прежний
    /// </summary>
    [Fact]
    public async Task RevokedTokenIsNotSent()
    {
        var oleg = Oleg("chuzhoi@shariki-i-palochki.ru");
        var token = Token(oleg.Id, AccountTokenKind.PasswordReset, OlegEmail, TimeSpan.FromHours(1));
        token.RevokedAtUtc = DateTime.UtcNow;
        var command = Command(new SendMailCommand { Kind = MailKind.PasswordReset, TokenId = token.Id });

        var db = await Given.WorkerClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(token);
            x.LocalQueue.Add(command);
        }, _mail.Services());

        await Processed(db, command);

        Assert.Empty(_mail.Sent);
    }

    /// <summary>«Почту сменили» — без токена, на прежний адрес из команды, со ссылкой восстановления</summary>
    [Fact]
    public async Task SendsEmailChangedToOldAddress()
    {
        var oleg = Oleg("new@roga-i-kopyta.ru");
        var command = Command(new SendMailCommand { Kind = MailKind.EmailChanged, To = OlegEmail, Language = "ru" });

        var db = await Given.WorkerClient(x =>
        {
            x.Profiles.Add(oleg);
            x.LocalQueue.Add(command);
        }, _mail.Services());

        await Processed(db, command);

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal(OlegEmail, mail.To);
        Assert.Contains("https://example.com/auth/recover", mail.Text);
    }

    /// <summary>
    /// Провайдер упал — письмо не теряется: строка остаётся в очереди и откладывается
    /// до повтора, токен не помечен отправленным
    /// </summary>
    [Fact]
    public async Task FailedSendStaysInQueue()
    {
        _mail.IsDown = true;
        var oleg = Oleg();
        var token = Token(oleg.Id, AccountTokenKind.EmailConfirm, OlegEmail, TimeSpan.FromDays(1));
        var command = Command(new SendMailCommand { Kind = MailKind.EmailConfirm, TokenId = token.Id });

        var db = await Given.WorkerClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(token);
            x.LocalQueue.Add(command);
        }, _mail.Services());

        // Откладывание пишет время повтора дальше блокировки выборки — его и ждём
        await Until(async () => await db.LocalQueue.AsNoTracking()
            .AnyAsync(x => x.Id == command.Id && x.TryCount > 0 && x.LockedTillUtc > DateTime.UtcNow.AddMinutes(-1)
                           && x.LockedTillUtc < DateTime.UtcNow.AddMinutes(1.5)));

        Assert.Empty(_mail.Sent);
        Assert.Null((await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == token.Id)).MailSentAtUtc);
    }

    private static Task Processed(ApplicationContext db, Db.Entity.LocalQueue command) =>
        Until(async () => !await db.LocalQueue.AsNoTracking().AnyAsync(x => x.Id == command.Id));

    private static async Task Until(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + ProcessingTimeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Очередь не обработала команду вовремя");
            await Task.Delay(50);
        }
    }
}
