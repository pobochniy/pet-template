using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Auth;

/// <summary>
/// Окно свежего входа (спец. accounts, 8.3): после входа через Telegram или VK новый пароль
/// задаётся без старого — так восстанавливают забытый пароль через привязанный вход. Окно
/// считается от <c>auth_time</c> в токене, а не от выдачи токена. Окно для привязки, отвязки,
/// почты и слияния и <c>Account/ConfirmPassword</c> (8.6) — <c>FreshLoginRequiredApiTests</c>
/// </summary>
public class FreshLoginApiTests
{
    private const long OlegTelegramId = 555000777;
    private const string NewPassword = "Novyi-parol-42";

    private readonly FakeVkId _vk = new();

    /// <summary>Олег с паролем, Telegram и VK — пароль задаётся без старого только в окне</summary>
    private PetTemplate.Db.Entity.Profile Oleg() =>
        new UserBuilder("Oleg", telegramId: OlegTelegramId).WithVk(_vk.VkUserId).Please().profile;

    private static async Task SignIn(HttpClient client, HttpResponseMessage login)
    {
        await login.ShouldBeSuccessful();
        var session = await login.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
    }

    private static Task<HttpResponseMessage> SetPassword(HttpClient client) =>
        client.PostAsJsonAsync("/api/Account/SetPassword",
            new SetPasswordDto { NewPassword = NewPassword, NewPasswordConfirm = NewPassword });

    private static ExternalCodeDto TelegramCode() => new()
    {
        Code = new TelegramLoginBuilder().WithUserId(OlegTelegramId).Build(),
        State = TelegramLoginBuilder.DefaultNonce
    };

    [Fact]
    public async Task FreshTelegramLoginReplacesPassword()
    {
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(Oleg()));
        await SignIn(client, await client.PostAsJsonAsync("/api/Auth/TelegramLogin", TelegramCode()));

        var response = await SetPassword(client);

        await response.ShouldBeSuccessful();
    }

    [Fact]
    public async Task FreshVkLoginReplacesPassword()
    {
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(Oleg()),
            settings: _vk.Settings(), configureServices: _vk.Services());
        await SignIn(client, await client.PostAsJsonAsync("/api/Auth/VkId",
            new ExternalCodeDto { Code = FakeVkId.ValidCode, Verifier = "v", DeviceId = "d", State = "s" }));

        var response = await SetPassword(client);

        await response.ShouldBeSuccessful();
    }

    /// <summary>
    /// Восстановление (8.3): вход только в существующий аккаунт — и тот же путь к паролю
    /// без старого. После него паролем входит новый, а не старый
    /// </summary>
    [Fact]
    public async Task RecoveryThroughTelegramSetsNewPassword()
    {
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(Oleg()));
        var code = TelegramCode();
        code.ExistingOnly = true;
        await SignIn(client, await client.PostAsJsonAsync("/api/Auth/TelegramLogin", code));

        await (await SetPassword(client)).ShouldBeSuccessful();

        client.DefaultRequestHeaders.Authorization = null;
        var login = await client.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto { Login = "Oleg", Password = NewPassword });
        await login.ShouldBeSuccessful();
    }

    /// <summary>
    /// Telegram привязан к аккаунту только что — свежий вход им пароль без текущего не задаёт:
    /// привязать его мог и тот, кто украл сессию
    /// </summary>
    [Fact]
    public async Task RecentlyLinkedLoginCannotReplacePassword()
    {
        var oleg = Oleg();
        oleg.User!.TelegramLinkedAtUtc = DateTime.UtcNow.AddHours(-1);
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg));
        await SignIn(client, await client.PostAsJsonAsync("/api/Auth/TelegramLogin", TelegramCode()));

        var response = await SetPassword(client);

        await response.ShouldBeBadRequestWith(AuthMessages.LoginRecentlyLinked);
        var logins = await client.GetFromJsonAsync<AccountLoginsDto>("/api/Account/Logins");
        Assert.False(logins!.CanSetPasswordWithoutCurrent);
    }

    /// <summary>Привязке больше суток — вход через неё снова восстанавливает пароль</summary>
    [Fact]
    public async Task SettledLinkReplacesPassword()
    {
        var oleg = Oleg();
        oleg.User!.TelegramLinkedAtUtc = DateTime.UtcNow.AddDays(-2);
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg));
        await SignIn(client, await client.PostAsJsonAsync("/api/Auth/TelegramLogin", TelegramCode()));

        var response = await SetPassword(client);

        await response.ShouldBeSuccessful();
    }

    /// <summary>Вход через площадку дольше окна назад — нужен старый пароль</summary>
    [Fact]
    public async Task StaleExternalLoginNeedsCurrentPassword()
    {
        var oleg = Oleg();
        var (client, _) = await TestSession.SignedInVia(oleg.Id,
            new ExternalLogin(ExternalProvider.Telegram, DateTimeOffset.UtcNow.AddMinutes(-11)), x => x.Profiles.Add(oleg));

        var response = await SetPassword(client);

        await response.ShouldBeBadRequestWith(AuthMessages.PasswordAlreadySet);
    }

    [Fact]
    public async Task FreshExternalLoginShowsInLogins()
    {
        var oleg = Oleg();
        var (client, _) = await TestSession.SignedInVia(oleg.Id,
            new ExternalLogin(ExternalProvider.Vk, DateTimeOffset.UtcNow.AddMinutes(-2)), x => x.Profiles.Add(oleg));

        var logins = await client.GetFromJsonAsync<AccountLoginsDto>("/api/Account/Logins");

        Assert.True(logins!.CanSetPasswordWithoutCurrent);
    }

    /// <summary>Токен входа через Telegram несёт время и способ входа</summary>
    [Fact]
    public async Task TokenCarriesLoginTimeAndMethod()
    {
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(Oleg()));

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", TelegramCode());

        var token = await Token(response);
        var authTime = long.Parse(token.Claims.Single(x => x.Type == FreshLogin.AuthTimeClaim).Value);
        Assert.InRange(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - authTime, 0, 60);
        Assert.Equal("telegram", token.Claims.Single(x => x.Type == FreshLogin.MethodClaim).Value);
    }

    /// <summary>
    /// Вход из mini-app — тоже вход площадкой, но время входа — время подписи запуска, а не
    /// выдачи токена: подпись живёт сутки, и вход давней подписью окна не открывает (8.3)
    /// </summary>
    [Fact]
    public async Task MiniAppLoginCarriesSignatureTime()
    {
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(Oleg()));
        var signedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var initData = new TelegramInitDataBuilder().WithUserId(OlegTelegramId)
            .WithBotToken(TestSettings.DefaultTelegramBotToken).SignedAt(signedAt).Please();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramAuth", new TelegramAuthDto { InitData = initData });

        var token = await Token(response);
        Assert.Equal(signedAt.ToUnixTimeSeconds(), long.Parse(token.Claims.Single(x => x.Type == FreshLogin.AuthTimeClaim).Value));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RawData);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetPassword(client)).StatusCode);
    }

    /// <summary>Вход паролем — тоже свежий вход (8.6), но способ в токене — пароль, а не площадка</summary>
    [Fact]
    public async Task PasswordLoginCarriesPasswordMethod()
    {
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(Oleg()));

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn",
            new LoginDto { Login = "Oleg", Password = UserBuilder.SuperAdminPassword });

        var token = await Token(response);
        var authTime = long.Parse(token.Claims.Single(x => x.Type == FreshLogin.AuthTimeClaim).Value);
        Assert.InRange(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - authTime, 0, 60);
        Assert.Equal(FreshLogin.PasswordMethod, token.Claims.Single(x => x.Type == FreshLogin.MethodClaim).Value);
    }

    /// <summary>Вход паролем окна не открывает: «задать» поверх старого — отказ</summary>
    [Fact]
    public async Task PasswordLoginDoesNotReplacePassword()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await SetPassword(client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<JwtSecurityToken> Token(HttpResponseMessage response)
    {
        await response.ShouldBeSuccessful();
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        return new JwtSecurityTokenHandler().ReadJwtToken(session!.Token);
    }
}
