using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Errors;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Account;

/// <summary>
/// Блок «Основное»: <c>Account/Logins</c>, <c>Account/Save</c>, привязка и отвязка Telegram
/// и VK (спец. accounts, разделы 3 и 6)
/// </summary>
public class LoginsApiTests
{
    private const string OlegEmail = "oleg@example.com";
    private const long MashaTelegramId = 555000111;

    private static ExternalProofDto TelegramProof(long telegramId) => new()
    {
        Provider = ExternalProvider.Telegram,
        Code = new TelegramLoginBuilder().WithUserId(telegramId).Build(),
        State = TelegramLoginBuilder.DefaultNonce
    };

    [Fact]
    public async Task LoginsShowWaysToSignIn()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithoutTelegram().WithContacts(OlegEmail, "+79990000001").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var logins = await client.GetFromJsonAsync<AccountLoginsDto>("/api/Account/Logins");

        Assert.Equal("Oleg", logins!.UserName);
        Assert.True(logins.HasPassword);
        Assert.False(logins.TelegramLinked);
        Assert.False(logins.VkLinked);
        Assert.Equal(1, logins.LoginCount);
        Assert.Equal(OlegEmail, logins.Email);
        Assert.False(logins.EmailConfirmed);
    }

    /// <summary>Пришедший из mini-app Telegram входит им — это способ войти (раздел 3)</summary>
    [Fact]
    public async Task TelegramAccountCountsTelegramAsWayToSignIn()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var logins = await client.GetFromJsonAsync<AccountLoginsDto>("/api/Account/Logins");

        Assert.True(logins!.TelegramLinked);
        Assert.Equal(2, logins.LoginCount);
    }

    /// <summary>Логин без пароля — не способ войти (раздел 3)</summary>
    [Fact]
    public async Task PasswordlessAccountHasNoWaysToSignIn()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutTelegram().WithoutPassword().Please();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow, x => x.Profiles.Add(oleg));

        var logins = await client.GetFromJsonAsync<AccountLoginsDto>("/api/Account/Logins");

        Assert.False(logins!.HasPassword);
        Assert.Equal(0, logins.LoginCount);
    }

    [Fact]
    public async Task CanSaveContacts()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithContacts(OlegEmail, "+79990000001").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto
        {
            Email = " olezhka@example.com ",
            Phone = "+79990000009",
            Comment = "Пишу код по ночам"
        });

        await response.ShouldBeSuccessful();
        var saved = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.Equal("olezhka@example.com", saved.Email);
        Assert.Equal("+79990000009", saved.PhoneNumber);
        Assert.Equal("Пишу код по ночам", saved.Comment);
    }

    /// <summary>Пустое поле — «убрать»: почта и телефон необязательны</summary>
    [Fact]
    public async Task EmptyContactsAreRemoved()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithContacts(OlegEmail, "+79990000001").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "", Phone = "" });

        await response.ShouldBeSuccessful();
        var saved = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.Null(saved.Email);
        Assert.Null(saved.PhoneNumber);
    }

    /// <summary>Смена почты сбрасывает её подтверждение (8.4)</summary>
    [Fact]
    public async Task ChangingEmailResetsConfirmation()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithContacts(OlegEmail, "+79990000001").Please();
        oleg.EmailConfirmed = true;
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "olezhka@example.com" });

        Assert.False((await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).EmailConfirmed);
    }

    /// <summary>Сохранение без смены почты подтверждение не трогает</summary>
    [Fact]
    public async Task KeepingEmailKeepsConfirmation()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithContacts(OlegEmail, "+79990000001").Please();
        oleg.EmailConfirmed = true;
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = OlegEmail, Comment = "Новый" });

        Assert.True((await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).EmailConfirmed);
    }

    /// <summary>
    /// Почта — у одного аккаунта (8.4): занятая — отказ с кодом, а не 500 индекса. Сравнение
    /// без учёта регистра — колонка <c>citext</c>
    /// </summary>
    [Fact]
    public async Task RejectsTakenEmailInOtherCase()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithContacts(OlegEmail, "+79990000001").Please();
        var (_, vasya) = new UserBuilder("Vasya").WithContacts("vasya@example.com", "+79990000002").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.AddRange(oleg, vasya), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "VASYA@example.com" });

        await response.ShouldBeBadRequestWith(AuthMessages.EmailTaken);
    }

    [Fact]
    public async Task RejectsTakenPhone()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithContacts(OlegEmail, "+79990000001").Please();
        var (_, vasya) = new UserBuilder("Vasya").WithContacts("vasya@example.com", "+79990000002").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.AddRange(oleg, vasya), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Phone = "+79990000002" });

        await response.ShouldBeBadRequestWith(AuthMessages.PhoneTaken);
    }

    /// <summary>Телефон — плюс и 11 цифр (<c>PhoneRule</c>): другим видом не войти</summary>
    [Fact]
    public async Task RejectsPhoneOfWrongFormat()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Phone = "8 999 000-00-01" });

        await response.ShouldBeValidationWith("phone", ValidationCodes.PhoneFormat);
    }

    [Fact]
    public async Task RejectsMalformedEmail()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "oleg-at-example" });

        await response.ShouldBeValidationWith("email", ValidationCodes.Email);
    }

    /// <summary>
    /// Почта до 255 символов держится проверкой: у <c>citext</c> в базе предела нет
    /// (спец. infrastructure, 3.1)
    /// </summary>
    [Fact]
    public async Task RejectsTooLongEmail()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save",
            new AccountSaveDto { Email = new string('o', 256) + "@example.com" });

        await response.ShouldBeValidationWith("email", ValidationCodes.StringLength);
    }

    [Fact]
    public async Task LoginsShowLinkedTelegramAndVk()
    {
        var (token, oleg) = new UserBuilder("Oleg", telegramId: MashaTelegramId).WithVk(2308481).Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var logins = await client.GetFromJsonAsync<AccountLoginsDto>("/api/Account/Logins");

        Assert.True(logins!.TelegramLinked);
        Assert.True(logins.VkLinked);
        Assert.Equal(3, logins.LoginCount);
        Assert.False(logins.CanSetPasswordWithoutCurrent);
    }

    /// <summary>Привязка — тем же подписанным входом, что вход на сайт (раздел 6)</summary>
    [Fact]
    public async Task CanLinkFreeTelegram()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Link", TelegramProof(MashaTelegramId));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<LinkResultDto>();
        Assert.True(result!.Logins!.TelegramLinked);
        Assert.Null(result.MergePreview);
        Assert.Equal(MashaTelegramId, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramId);
    }

    /// <summary>Привязка VK — кодом VK ID с PKCE, как вход</summary>
    [Fact]
    public async Task CanLinkFreeVk()
    {
        var vk = new FakeVkId();
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token,
            settings: vk.Settings(), configureServices: vk.Services());

        var response = await client.PostAsJsonAsync("/api/Account/Link", new ExternalProofDto
        {
            Provider = ExternalProvider.Vk,
            Code = FakeVkId.ValidCode,
            Verifier = "pkce-verifier",
            DeviceId = "device-1",
            State = "st-1"
        });

        await response.ShouldBeSuccessful();
        Assert.Equal(vk.VkUserId, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).VkId);
    }

    /// <summary>
    /// Привязка изнутри mini-app — подписанными параметрами запуска, а не окном Telegram:
    /// снаружи окна там нет (раздел 6)
    /// </summary>
    [Fact]
    public async Task CanLinkTelegramFromMiniApp()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Link", new ExternalProofDto
        {
            Provider = ExternalProvider.Telegram,
            InitData = new TelegramInitDataBuilder().WithUserId(MashaTelegramId)
                .WithBotToken(TestSettings.DefaultTelegramBotToken).Please()
        });

        await response.ShouldBeSuccessful();
        Assert.Equal(MashaTelegramId, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramId);
    }

    /// <summary>Подделанный вход ничего не привязывает</summary>
    [Fact]
    public async Task CannotLinkWithForgedProof()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);
        var proof = TelegramProof(MashaTelegramId);
        proof.State = "another-state";

        var response = await client.PostAsJsonAsync("/api/Account/Link", proof);

        await response.ShouldBeBadRequestWith(AuthMessages.ExternalFailed);
    }

    /// <summary>
    /// Telegram уже вошёл в другой аккаунт — привязки нет, в ответе просмотр слияния
    /// с этим аккаунтом (разделы 6 и 7.1): код площадки одноразовый, второй раз им не доказать
    /// </summary>
    [Fact]
    public async Task LinkingTakenTelegramOpensMerge()
    {
        var (token, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (_, masha) = new UserBuilder("Masha", telegramId: MashaTelegramId).Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.AddRange(oleg, masha), token);

        var response = await client.PostAsJsonAsync("/api/Account/Link", TelegramProof(MashaTelegramId));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<LinkResultDto>();
        Assert.Null(result!.Logins);
        Assert.Equal(oleg.Id, result.MergePreview!.Current.UserId);
        Assert.Equal(masha.Id, result.MergePreview.Other.UserId);
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramId);
    }

    /// <summary>Один Telegram на аккаунт: другой — сначала отвязать текущий (раздел 3)</summary>
    [Fact]
    public async Task CannotLinkSecondTelegram()
    {
        var (token, oleg) = new UserBuilder("Oleg", telegramId: 42).Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Link", TelegramProof(MashaTelegramId));

        await response.ShouldBeBadRequestWith(AuthMessages.LoginAlreadyLinked);
    }

    [Fact]
    public async Task CanUnlinkWhenPasswordRemains()
    {
        var (token, oleg) = new UserBuilder("Oleg", telegramId: MashaTelegramId).Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Telegram });

        await response.ShouldBeSuccessful();
        var logins = await response.Content.ReadFromJsonAsync<AccountLoginsDto>();
        Assert.False(logins!.TelegramLinked);
    }

    /// <summary>Последний способ войти не отвязывается: аккаунт стал бы недостижим (раздел 3)</summary>
    [Fact]
    public async Task CannotUnlinkLastLogin()
    {
        var (_, oleg) = new UserBuilder("Oleg", telegramId: MashaTelegramId).WithoutPassword().Please();
        var (client, db) = await FreshTelegramSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Telegram });

        await response.ShouldBeBadRequestWith(AuthMessages.LastLogin);
        Assert.NotNull((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramId);
    }

    /// <summary>Второй вход остаётся — первый отвязывается и без пароля</summary>
    [Fact]
    public async Task CanUnlinkTelegramWhenVkRemains()
    {
        var (_, oleg) = new UserBuilder("Oleg", telegramId: MashaTelegramId).WithVk(2308481).WithoutPassword().Please();
        var (client, _) = await FreshTelegramSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Telegram });

        await response.ShouldBeSuccessful();
    }

    /// <summary>
    /// Отвязка оставляет аккаунту логин <c>tg&lt;id&gt;</c> (4.2): он уже чей-то, и тот же
    /// Telegram, придя снова, получит хвост (<c>ExternalRegistrationTests</c>)
    /// </summary>
    [Fact]
    public async Task UnlinkKeepsGeneratedUserName()
    {
        var (token, oleg) = new UserBuilder($"tg{MashaTelegramId}", telegramId: MashaTelegramId).Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Telegram });

        Assert.Equal($"tg{MashaTelegramId}", (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).UserName);
    }

    /// <summary>
    /// Аккаунт без пароля подтверждает владение только свежим входом площадки (спец. accounts,
    /// 8.6) — сессия, открытая Telegram минуту назад
    /// </summary>
    private static Task<(HttpClient client, ApplicationContext db)> FreshTelegramSession(PetTemplate.Db.Entity.Profile oleg) =>
        TestSession.SignedInVia(oleg.Id, new ExternalLogin(ExternalProvider.Telegram, DateTimeOffset.UtcNow.AddMinutes(-1)),
            x => x.Profiles.Add(oleg));
}
