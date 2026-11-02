using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Mail;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Account;

/// <summary>Подтверждение почты (спец. accounts, 8.4; mail, 3 и 4)</summary>
public class EmailConfirmApiTests
{
    private const string Raw = "raw-confirm-token";
    private const string OlegEmail = "oleg@roga-i-kopyta.ru";

    /// <summary>Токен подтверждения так, как его оставил обработчик письма: в базе — хеш</summary>
    private static AccountToken ConfirmToken(Guid userId, string email = OlegEmail, DateTime? expires = null) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Kind = AccountTokenKind.EmailConfirm,
        Hash = AccountTokens.HashOf(Raw),
        Email = email,
        CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        ExpiresAtUtc = expires ?? DateTime.UtcNow.AddDays(1)
    };

    private static (string token, PetTemplate.Db.Entity.Profile oleg) Oleg(bool confirmed = false)
    {
        var (token, oleg) = new UserBuilder("Oleg").WithContacts(OlegEmail, "+79990000001").Please();
        oleg.EmailConfirmed = confirmed;
        return (token, oleg);
    }

    private static Task<HttpResponseMessage> Confirm(HttpClient client, string raw = Raw) =>
        client.PostAsJsonAsync("/api/Auth/ConfirmEmail", new EmailTokenDto { Token = raw });

    private static RegisterDto Register(string? email = null, string? language = null) => new()
    {
        UserName = "Oleg",
        Email = email,
        Password = "Sekretnyi-1",
        PasswordConfirm = "Sekretnyi-1",
        Language = language
    };

    [Fact]
    public async Task RegistrationWithEmailQueuesConfirmation()
    {
        var (client, db) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/Register", Register(OlegEmail));

        await response.ShouldBeSuccessful();
        var mail = Assert.Single(await QueueAssert.Mails(db));
        Assert.Equal(MailKind.EmailConfirm, mail.Kind);
        var token = await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == mail.TokenId);
        // Сам токен придумает обработчик письма — в строке очереди и в базе его нет
        Assert.Null(token.Hash);
        Assert.Equal(OlegEmail, token.Email);
    }

    [Fact]
    public async Task RegistrationWithoutEmailSendsNothing()
    {
        var (client, db) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/Register", Register());

        await response.ShouldBeSuccessful();
        Assert.Empty(await QueueAssert.Mails(db));
    }

    /// <summary>Язык регистрации ложится в профиль — на нём уйдут письма (8.4)</summary>
    [Fact]
    public async Task RegistrationKeepsLanguage()
    {
        var (client, db) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/Register", Register(language: "EN"));

        await response.ShouldBeSuccessful();
        Assert.Equal("en", (await db.Profiles.AsNoTracking().SingleAsync(x => x.UserName == "Oleg")).Language);
    }

    /// <summary>Незнакомый язык регистрацию не отбивает: его прислал браузер — ложится русский</summary>
    [Fact]
    public async Task RegistrationWithUnknownLanguageFallsBackToRussian()
    {
        var (client, db) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/Register", Register(language: "de"));

        await response.ShouldBeSuccessful();
        Assert.Equal(LanguageConstants.Russian, (await db.Profiles.AsNoTracking().SingleAsync(x => x.UserName == "Oleg")).Language);
    }

    [Fact]
    public async Task ConfirmsEmailByLink()
    {
        var (_, oleg) = Oleg();
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ConfirmToken(oleg.Id));
        });

        var response = await Confirm(client);

        await response.ShouldBeSuccessful();
        Assert.True((await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).EmailConfirmed);
    }

    [Fact]
    public async Task LinkWorksOnce()
    {
        var (_, oleg) = Oleg();
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ConfirmToken(oleg.Id));
        });
        await (await Confirm(client)).ShouldBeSuccessful();

        var response = await Confirm(client);

        await response.ShouldBeBadRequestWith(AuthMessages.LinkInvalid);
    }

    /// <summary>
    /// Ссылка одноразовая и при одновременных переходах (8.4): расход — условным UPDATE,
    /// и из запросов, увидевших живой токен, строку меняет только один
    /// </summary>
    [Fact]
    public async Task LinkIsConsumedOnceEvenConcurrently()
    {
        var (_, oleg) = Oleg();
        var token = ConfirmToken(oleg.Id);
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(token);
        });

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Confirm(client)));

        Assert.Single(responses, x => x.IsSuccessStatusCode);
        Assert.All(responses.Where(x => !x.IsSuccessStatusCode), x => Assert.Equal(HttpStatusCode.BadRequest, x.StatusCode));
        Assert.NotNull((await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == token.Id)).UsedAtUtc);
    }

    [Fact]
    public async Task ExpiredLinkFails()
    {
        var (_, oleg) = Oleg();
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ConfirmToken(oleg.Id, expires: DateTime.UtcNow.AddMinutes(-1)));
        });

        var response = await Confirm(client);

        await response.ShouldBeBadRequestWith(AuthMessages.LinkInvalid);
    }

    /// <summary>Ссылка на адрес, который с тех пор сменили, не подтверждает новый</summary>
    [Fact]
    public async Task LinkForOldAddressFails()
    {
        var (_, oleg) = Oleg();
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ConfirmToken(oleg.Id, email: "staryi@roga-i-kopyta.ru"));
        });

        var response = await Confirm(client);

        await response.ShouldBeBadRequestWith(AuthMessages.LinkInvalid);
        Assert.False((await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).EmailConfirmed);
    }

    /// <summary>
    /// Смена почты сбрасывает подтверждение, новый адрес получает ссылку, а прежний
    /// подтверждённый — «Почту сменили»
    /// </summary>
    [Fact]
    public async Task ChangeResetsConfirmationAndWarnsOldAddress()
    {
        var (token, oleg) = Oleg(confirmed: true);
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "new@roga-i-kopyta.ru" });

        await response.ShouldBeSuccessful();
        Assert.False((await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).EmailConfirmed);
        var mails = await QueueAssert.Mails(db);
        Assert.Contains(mails, x => x.Kind == MailKind.EmailChanged && x.To == OlegEmail);
        Assert.Contains(mails, x => x.Kind == MailKind.EmailConfirm);
    }

    /// <summary>Неподтверждённый прежний адрес «Почту сменили» не получает: письма — только на подтверждённые</summary>
    [Fact]
    public async Task UnconfirmedOldAddressIsNotWarned()
    {
        var (token, oleg) = Oleg();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "new@roga-i-kopyta.ru" });

        await response.ShouldBeSuccessful();
        Assert.DoesNotContain(await QueueAssert.Mails(db), x => x.Kind == MailKind.EmailChanged);
    }

    /// <summary>Сохранение без смены почты (в другом регистре — тоже) писем не шлёт и подтверждение не трогает</summary>
    [Fact]
    public async Task SaveWithSameEmailSendsNothing()
    {
        var (token, oleg) = Oleg(confirmed: true);
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save",
            new AccountSaveDto { Email = OlegEmail.ToUpperInvariant(), Comment = "Привет" });

        await response.ShouldBeSuccessful();
        Assert.True((await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).EmailConfirmed);
        Assert.Empty(await QueueAssert.Mails(db));
    }

    /// <summary>«Отправить ещё раз» ставит письмо</summary>
    [Fact]
    public async Task ResendQueuesConfirmation()
    {
        var (token, oleg) = Oleg();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsync("/api/Account/SendEmailConfirmation", null);

        await response.ShouldBeSuccessful();
        Assert.Equal(MailKind.EmailConfirm, Assert.Single(await QueueAssert.Mails(db)).Kind);
    }

    /// <summary>Новая ссылка подтверждения гасит прежнюю: подтверждается последнее письмо</summary>
    [Fact]
    public async Task ResendRevokesPreviousLink()
    {
        var (token, oleg) = Oleg();
        var previous = ConfirmToken(oleg.Id);
        previous.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(previous);
        }, token);

        var response = await client.PostAsync("/api/Account/SendEmailConfirmation", null);

        await response.ShouldBeSuccessful();
        Assert.NotNull((await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == previous.Id)).RevokedAtUtc);
    }

    /// <summary>«Отправить ещё раз» не чаще раза в две минуты (8.4)</summary>
    [Fact]
    public async Task ResendIsLimitedPerTwoMinutes()
    {
        var (token, oleg) = Oleg();
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            var recent = ConfirmToken(oleg.Id);
            recent.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-30);
            x.AccountTokens.Add(recent);
        }, token);

        var response = await client.PostAsync("/api/Account/SendEmailConfirmation", null);

        await response.ShouldBeBadRequestWith(AuthMessages.TooManyEmails);
    }

    /// <summary>И не больше пяти в сутки (8.4)</summary>
    [Fact]
    public async Task ResendIsLimitedPerDay()
    {
        var (token, oleg) = Oleg();
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            for (var i = 1; i <= EmailConfirmation.PerDay; i++)
            {
                var earlier = ConfirmToken(oleg.Id);
                earlier.Hash = null;
                earlier.CreatedAtUtc = DateTime.UtcNow.AddHours(-i);
                x.AccountTokens.Add(earlier);
            }
        }, token);

        var response = await client.PostAsync("/api/Account/SendEmailConfirmation", null);

        await response.ShouldBeBadRequestWith(AuthMessages.TooManyEmails);
    }

    /// <summary>
    /// Счёт подтверждения свой, не общий со сбросом (8.4): чужие запросы сброса на адрес
    /// владельца не запирают ему «Отправить ещё раз»
    /// </summary>
    [Fact]
    public async Task ResetRequestsDoNotCountAgainstConfirmation()
    {
        var (token, oleg) = Oleg();
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            var reset = ConfirmToken(oleg.Id);
            reset.Kind = AccountTokenKind.PasswordReset;
            reset.Hash = null;
            reset.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-30);
            x.AccountTokens.Add(reset);
        }, token);

        var response = await client.PostAsync("/api/Account/SendEmailConfirmation", null);

        await response.ShouldBeSuccessful();
        Assert.Single(await QueueAssert.Mails(db));
    }

    [Fact]
    public async Task ResendWithoutEmailFails()
    {
        var (token, oleg) = Oleg();
        oleg.Email = null;
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsync("/api/Account/SendEmailConfirmation", null);

        await response.ShouldBeBadRequestWith(AuthMessages.NoEmail);
    }

    /// <summary>
    /// Чужой неподтверждённый адрес тоже занят (8.4): почта — логин того аккаунта.
    /// Освобождает её поддержка
    /// </summary>
    [Fact]
    public async Task RejectsEmailUnconfirmedByAnotherAccount()
    {
        var (token, oleg) = Oleg();
        var (_, vasya) = new UserBuilder("Vasya").WithContacts("vasya@roga-i-kopyta.ru", "+79990000002").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.AddRange(oleg, vasya), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "Vasya@Roga-i-Kopyta.ru" });

        await response.ShouldBeBadRequestWith(AuthMessages.EmailTaken);
        Assert.Equal(OlegEmail, (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).Email);
    }

    /// <summary>
    /// Смена почты — под тем же лимитом писем, что «Отправить ещё раз» (8.4): иначе
    /// смена туда-сюда обходила бы его. Отказ тот же, почта не меняется
    /// </summary>
    [Fact]
    public async Task ChangeIsLimitedLikeResend()
    {
        var (token, oleg) = Oleg();
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            var recent = ConfirmToken(oleg.Id);
            recent.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-30);
            x.AccountTokens.Add(recent);
        }, token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "new@roga-i-kopyta.ru" });

        await response.ShouldBeBadRequestWith(AuthMessages.TooManyEmails);
        Assert.Equal(OlegEmail, (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).Email);
        Assert.Empty(await QueueAssert.Mails(db));
    }

    /// <summary>Убрали почту — ссылка подтверждения прежнего адреса больше не действует</summary>
    [Fact]
    public async Task RemovingEmailRevokesConfirmLink()
    {
        var (token, oleg) = Oleg();
        var link = ConfirmToken(oleg.Id);
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(link);
        }, token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "" });

        await response.ShouldBeSuccessful();
        Assert.NotNull((await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == link.Id)).RevokedAtUtc);
    }

    /// <summary>
    /// Смена почты гасит живые ссылки сброса: они выписаны на прежний адрес, и письмо по
    /// ним, ещё не ушедшее из очереди, не должно уйти никуда
    /// </summary>
    [Fact]
    public async Task ChangeRevokesResetLinks()
    {
        var (token, oleg) = Oleg(confirmed: true);
        var reset = new AccountToken
        {
            Id = Guid.CreateVersion7(),
            UserId = oleg.Id,
            Kind = AccountTokenKind.PasswordReset,
            Email = OlegEmail,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(reset);
        }, token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "new@roga-i-kopyta.ru" });

        await response.ShouldBeSuccessful();
        Assert.NotNull((await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == reset.Id)).RevokedAtUtc);
    }
}
