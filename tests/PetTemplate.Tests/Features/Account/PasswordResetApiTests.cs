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

/// <summary>
/// Восстановление пароля по ссылке — <c>Auth/RequestReset</c>, <c>Auth/Reset</c>
/// (спец. accounts, 8.2, 8.4)
/// </summary>
public class PasswordResetApiTests
{
    private const string Raw = "raw-reset-token";
    private const string NewPassword = "Novyi-parol-42";
    private const string OlegEmail = "oleg@roga-i-kopyta.ru";

    private static PetTemplate.Db.Entity.Profile Oleg(bool confirmed = true)
    {
        var (_, oleg) = new UserBuilder("Oleg").WithContacts(OlegEmail, "+79990000001").Please();
        oleg.EmailConfirmed = confirmed;
        return oleg;
    }

    private static AccountToken ResetToken(Guid userId, TimeSpan? life = null, DateTime? createdAt = null, string raw = Raw)
    {
        var created = createdAt ?? DateTime.UtcNow.AddMinutes(-5);
        return new AccountToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Kind = AccountTokenKind.PasswordReset,
            Hash = AccountTokens.HashOf(raw),
            Email = OlegEmail,
            CreatedAtUtc = created,
            ExpiresAtUtc = created + (life ?? TimeSpan.FromHours(1))
        };
    }

    private static ResetPasswordDto Reset(string token = Raw) =>
        new() { Token = token, NewPassword = NewPassword, NewPasswordConfirm = NewPassword };

    private static Task<HttpResponseMessage> RequestReset(HttpClient client, string email = OlegEmail) =>
        client.PostAsJsonAsync("/api/Auth/RequestReset", new RequestResetDto { Email = email });

    [Fact]
    public async Task SendsLinkToConfirmedEmail()
    {
        var oleg = Oleg();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await RequestReset(client);

        await response.ShouldBeSuccessful();
        var mail = Assert.Single(await QueueAssert.Mails(db));
        Assert.Equal(MailKind.PasswordReset, mail.Kind);
        // Адрес — в токене: подтверждённый в момент запроса (8.4)
        var token = await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == mail.TokenId);
        Assert.Equal(OlegEmail, token.Email);
        Assert.Null(mail.To);
    }

    /// <summary>Почта — `citext`: адрес в другом регистре находит тот же аккаунт</summary>
    [Fact]
    public async Task FindsAccountIgnoringCase()
    {
        var oleg = Oleg();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await RequestReset(client, OlegEmail.ToUpperInvariant());

        await response.ShouldBeSuccessful();
        Assert.Single(await QueueAssert.Mails(db));
    }

    /// <summary>Неподтверждённая почта — не доказательство: письма нет (8.4)</summary>
    [Fact]
    public async Task NoMailToUnconfirmedEmail()
    {
        var oleg = Oleg(confirmed: false);
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await RequestReset(client);

        await response.ShouldBeSuccessful();
        Assert.Empty(await QueueAssert.Mails(db));
    }

    /// <summary>Форма не выдаёт, есть ли аккаунт: ответ тот же, что у существующего</summary>
    [Fact]
    public async Task AnswersTheSameForUnknownEmail()
    {
        var oleg = Oleg();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var known = await RequestReset(client);
        var unknown = await RequestReset(client, "nobody@example.com");

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    /// <summary>Логин вместо почты — не запрос сброса: письмо уходит на почту, искать надо по ней</summary>
    [Fact]
    public async Task LoginIsNotAcceptedInsteadOfEmail()
    {
        var oleg = Oleg();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/RequestReset", new { email = "Oleg" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await QueueAssert.Mails(db));
    }

    /// <summary>Не чаще письма в две минуты на аккаунт — сверх лимита тот же ответ, письма нет</summary>
    [Fact]
    public async Task AccountLimitStopsMail()
    {
        var oleg = Oleg();
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ResetToken(oleg.Id, createdAt: DateTime.UtcNow.AddSeconds(-30)));
        });

        var response = await RequestReset(client);

        await response.ShouldBeSuccessful();
        Assert.Empty(await QueueAssert.Mails(db));
    }

    /// <summary>
    /// Счёт у сброса свой, не общий с подтверждением (8.4): только что ушло письмо
    /// подтверждения — сброс всё равно уходит
    /// </summary>
    [Fact]
    public async Task LimitIsNotSharedWithConfirmation()
    {
        var oleg = Oleg();
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            var confirm = ResetToken(oleg.Id, createdAt: DateTime.UtcNow.AddSeconds(-30));
            confirm.Kind = AccountTokenKind.EmailConfirm;
            x.AccountTokens.Add(confirm);
        });

        var response = await RequestReset(client);

        await response.ShouldBeSuccessful();
        Assert.Equal(MailKind.PasswordReset, Assert.Single(await QueueAssert.Mails(db)).Kind);
    }

    /// <summary>
    /// Новый запрос не гасит прежнюю живую ссылку (8.4): иначе чужие запросы гасили бы
    /// ссылку, которую владелец уже открыл
    /// </summary>
    [Fact]
    public async Task NewRequestKeepsPreviousLink()
    {
        var oleg = Oleg();
        var previous = ResetToken(oleg.Id, createdAt: DateTime.UtcNow.AddMinutes(-10));
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(previous);
        });

        var response = await RequestReset(client);

        await response.ShouldBeSuccessful();
        Assert.Single(await QueueAssert.Mails(db));
        Assert.Null((await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == previous.Id)).RevokedAtUtc);
    }

    /// <summary>Не больше 20 запросов в час с адреса — дальше письма не уходят</summary>
    [Fact]
    public async Task AddressLimitStopsMail()
    {
        var oleg = Oleg();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));
        for (var i = 0; i < 20; i++)
        {
            await RequestReset(client, $"nobody{i}@example.com");
        }

        var response = await RequestReset(client);

        await response.ShouldBeSuccessful();
        Assert.Empty(await QueueAssert.Mails(db));
    }

    [Fact]
    public async Task ResetSetsPasswordAndSignsIn()
    {
        var oleg = Oleg();
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ResetToken(oleg.Id));
        });
        var reset = await client.PostAsJsonAsync("/api/Auth/Reset", Reset());
        await reset.ShouldBeSuccessful();
        var session = await reset.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.False(string.IsNullOrEmpty(session!.Token));

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto { Login = "Oleg", Password = NewPassword });

        await response.ShouldBeSuccessful();
    }

    [Fact]
    public async Task LinkWorksOnce()
    {
        var oleg = Oleg();
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ResetToken(oleg.Id));
        });
        await (await client.PostAsJsonAsync("/api/Auth/Reset", Reset())).ShouldBeSuccessful();

        var response = await client.PostAsJsonAsync("/api/Auth/Reset", Reset());

        await response.ShouldBeBadRequestWith(AuthMessages.LinkInvalid);
    }

    /// <summary>Сброс гасит остальные живые ссылки сброса — и из писем, и от поддержки (8.4)</summary>
    [Fact]
    public async Task ResetRevokesOtherLinks()
    {
        var oleg = Oleg();
        var other = ResetToken(oleg.Id, raw: "other-reset-token");
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.AddRange(ResetToken(oleg.Id), other);
        });
        await (await client.PostAsJsonAsync("/api/Auth/Reset", Reset())).ShouldBeSuccessful();

        var response = await client.PostAsJsonAsync("/api/Auth/Reset", Reset("other-reset-token"));

        await response.ShouldBeBadRequestWith(AuthMessages.LinkInvalid);
        Assert.NotNull((await db.AccountTokens.AsNoTracking().SingleAsync(x => x.Id == other.Id)).RevokedAtUtc);
    }

    /// <summary>Сброс завершает остальные сессии (8.1)</summary>
    [Fact]
    public async Task ResetRevokesOldSessions()
    {
        var oleg = Oleg();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow.AddMinutes(-5), x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ResetToken(oleg.Id));
        });
        await (await client.PostAsJsonAsync("/api/Auth/Reset", Reset())).ShouldBeSuccessful();

        var response = await client.GetAsync("/api/Account/Logins");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Ссылка письма живёт час (8.2)</summary>
    [Fact]
    public async Task ExpiredLinkFails()
    {
        var oleg = Oleg();
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ResetToken(oleg.Id, createdAt: DateTime.UtcNow.AddHours(-2)));
        });

        var response = await client.PostAsJsonAsync("/api/Auth/Reset", Reset());

        await response.ShouldBeBadRequestWith(AuthMessages.LinkInvalid);
    }

    /// <summary>Ссылка от поддержки живёт сутки — через два часа ещё работает (8.2)</summary>
    [Fact]
    public async Task SupportLinkLivesLonger()
    {
        var oleg = Oleg();
        var (_, boris) = new UserBuilder("Boris").WithContacts("boris@roga-i-kopyta.ru", "+79990000002").Please();
        var token = ResetToken(oleg.Id, life: TimeSpan.FromHours(24), createdAt: DateTime.UtcNow.AddHours(-2));
        token.IssuedByUserId = boris.Id;
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.AddRange(oleg, boris);
            x.AccountTokens.Add(token);
        });

        var response = await client.PostAsJsonAsync("/api/Auth/Reset", Reset());

        await response.ShouldBeSuccessful();
    }

    /// <summary>Новый пароль — по тем же правилам, что везде (8.1)</summary>
    [Fact]
    public async Task RejectsShortPassword()
    {
        var oleg = Oleg();
        var (client, _) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(oleg);
            x.AccountTokens.Add(ResetToken(oleg.Id));
        });

        var response = await client.PostAsJsonAsync("/api/Auth/Reset",
            new ResetPasswordDto { Token = Raw, NewPassword = "korotk", NewPasswordConfirm = "korotk" });

        await response.ShouldBeValidation(["newPassword"]);
    }
}
