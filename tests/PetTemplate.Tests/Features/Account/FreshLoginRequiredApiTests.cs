using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Account;

/// <summary>
/// Подтверждение владения (спец. accounts, 8.6): привязка, отвязка, смена почты и слияние — только
/// в окне свежего входа паролем или площадкой, привязанной не только что. Живой, но давний
/// токен — <c>auth.error.freshLoginRequired</c>: с украденной сессией иначе привязывали бы
/// свой вход и забирали аккаунт
/// </summary>
public class FreshLoginRequiredApiTests
{
    private const string OlegEmail = "oleg@example.com";
    private const long OlegTelegramId = 555000222;
    private const long OlegVkId = 2308482;
    private const long StrangerTelegramId = 555000333;

    private static PetTemplate.Db.Entity.Profile Oleg() =>
        new UserBuilder("Oleg", telegramId: OlegTelegramId).WithVk(OlegVkId)
            .WithContacts(OlegEmail, "+79990000001").Please().profile;

    /// <summary>Сессия без отметки входа — как токен, перевыпущенный созданием пространства</summary>
    private static Task<(HttpClient client, ApplicationContext db)> StaleSession(PetTemplate.Db.Entity.Profile oleg) =>
        TestSession.IssuedAt(oleg.Id, DateTime.UtcNow, x => x.Profiles.Add(oleg));

    private static ExternalProofDto TelegramProof(long telegramId) => new()
    {
        Provider = ExternalProvider.Telegram,
        Code = new TelegramLoginBuilder().WithUserId(telegramId).Build(),
        State = TelegramLoginBuilder.DefaultNonce
    };

    [Fact]
    public async Task StaleSessionCannotLink()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (client, db) = await StaleSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/Link", TelegramProof(StrangerTelegramId));

        await response.ShouldBeBadRequestWith(AuthMessages.FreshLoginRequired);
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramId);
    }

    [Fact]
    public async Task StaleSessionCannotUnlink()
    {
        var oleg = Oleg();
        var (client, db) = await StaleSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Vk });

        await response.ShouldBeBadRequestWith(AuthMessages.FreshLoginRequired);
        Assert.NotNull((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).VkId);
    }

    /// <summary>Смена почты отказывает целиком — телефон из того же запроса не ложится</summary>
    [Fact]
    public async Task StaleSessionCannotChangeEmail()
    {
        var oleg = Oleg();
        var (client, db) = await StaleSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/Save",
            new AccountSaveDto { Email = "thief@example.com", Phone = "+79990000077" });

        await response.ShouldBeBadRequestWith(AuthMessages.FreshLoginRequired);
        var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.Equal(OlegEmail, profile.Email);
        Assert.NotEqual("+79990000077", profile.PhoneNumber);
    }

    /// <summary>Убрать почту — тоже смена: без неё сброс по письму владельцу уже не придёт</summary>
    [Fact]
    public async Task StaleSessionCannotRemoveEmail()
    {
        var oleg = Oleg();
        var (client, db) = await StaleSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = null });

        await response.ShouldBeBadRequestWith(AuthMessages.FreshLoginRequired);
        Assert.Equal(OlegEmail, (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).Email);
    }

    /// <summary>Та же почта (с другим регистром и пробелами) — не смена: телефон сохраняется и без свежего входа</summary>
    [Fact]
    public async Task StaleSessionSavesContactsWithSameEmail()
    {
        var oleg = Oleg();
        var (client, db) = await StaleSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/Save",
            new AccountSaveDto { Email = " OLEG@example.com ", Phone = "+79990000077" });

        await response.ShouldBeSuccessful();
        Assert.Equal("+79990000077", (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).PhoneNumber);
    }

    /// <summary>
    /// Просмотр слияния отказывает до проверки второго аккаунта: давняя сессия не начинает
    /// слияние даже с верным паролем второго
    /// </summary>
    [Fact]
    public async Task StaleSessionCannotPreviewMerge()
    {
        var oleg = Oleg();
        var (_, masha) = new UserBuilder("Masha").Please();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow, x => x.Profiles.AddRange(oleg, masha));

        var response = await client.PostAsJsonAsync("/api/Account/MergePreview",
            new MergeProofDto { Login = "Masha", Password = UserBuilder.SuperAdminPassword });

        await response.ShouldBeBadRequestWith(AuthMessages.FreshLoginRequired);
    }

    /// <summary>Пароль подтверждает владение: новая сессия открывает отвязку</summary>
    [Fact]
    public async Task ConfirmedPasswordOpensUnlink()
    {
        var oleg = Oleg();
        var (client, db) = await StaleSession(oleg);

        await UseSession(client, await client.PostAsJsonAsync("/api/Account/ConfirmPassword",
            new ConfirmPasswordDto { Password = UserBuilder.SuperAdminPassword }));
        var response = await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Vk });

        await response.ShouldBeSuccessful();
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).VkId);
    }

    /// <summary>После подтверждения привязка проходит — и запоминает, когда вход привязан (8.3)</summary>
    [Fact]
    public async Task ConfirmedPasswordOpensLink()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (client, db) = await StaleSession(oleg);

        await UseSession(client, await client.PostAsJsonAsync("/api/Account/ConfirmPassword",
            new ConfirmPasswordDto { Password = UserBuilder.SuperAdminPassword }));
        var response = await client.PostAsJsonAsync("/api/Account/Link", TelegramProof(StrangerTelegramId));

        await response.ShouldBeSuccessful();
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.Equal(StrangerTelegramId, user.TelegramId);
        Assert.NotNull(user.TelegramLinkedAtUtc);
    }

    /// <summary>После подтверждения почта меняется</summary>
    [Fact]
    public async Task ConfirmedPasswordOpensEmailChange()
    {
        var oleg = Oleg();
        var (client, db) = await StaleSession(oleg);

        await UseSession(client, await client.PostAsJsonAsync("/api/Account/ConfirmPassword",
            new ConfirmPasswordDto { Password = UserBuilder.SuperAdminPassword }));
        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Email = "new@example.com" });

        await response.ShouldBeSuccessful();
        Assert.Equal("new@example.com", (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).Email);
    }

    [Fact]
    public async Task WrongPasswordIsRefusedAtField()
    {
        var oleg = Oleg();
        var (client, _) = await StaleSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/ConfirmPassword",
            new ConfirmPasswordDto { Password = "not-the-password" });

        await response.ShouldBeValidationWith("password", AuthMessages.WrongPassword.Code);
    }

    /// <summary>
    /// Подбор пароля через украденную сессию упирается в лимит входа (8.1) — верный пароль
    /// тоже не проходит
    /// </summary>
    [Fact]
    public async Task ConfirmPasswordIsRateLimited()
    {
        var oleg = Oleg();
        var (client, _) = await StaleSession(oleg);

        for (var i = 0; i < LoginLimiter.ProductAttemptsPerUser; i++)
        {
            await client.PostAsJsonAsync("/api/Account/ConfirmPassword", new ConfirmPasswordDto { Password = $"guess-{i}" });
        }

        var response = await client.PostAsJsonAsync("/api/Account/ConfirmPassword",
            new ConfirmPasswordDto { Password = UserBuilder.SuperAdminPassword });

        await response.ShouldBeBadRequestWith(AuthMessages.TooManyAttempts);
    }

    /// <summary>Аккаунт без пароля паролем не подтверждает — только новым входом площадки</summary>
    [Fact]
    public async Task AccountWithoutPasswordCannotConfirmByPassword()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutPassword().Please();
        var (client, _) = await StaleSession(oleg);

        var response = await client.PostAsJsonAsync("/api/Account/ConfirmPassword",
            new ConfirmPasswordDto { Password = UserBuilder.SuperAdminPassword });

        await response.ShouldBeValidationWith("password", AuthMessages.WrongPassword.Code);
    }

    /// <summary>Свежий вход площадкой, привязанной давно, — тоже подтверждение</summary>
    [Fact]
    public async Task FreshSettledExternalLoginOpensUnlink()
    {
        var oleg = Oleg();
        var (client, _) = await TestSession.SignedInVia(oleg.Id,
            new ExternalLogin(ExternalProvider.Telegram, DateTimeOffset.UtcNow.AddMinutes(-1)), x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Vk });

        await response.ShouldBeSuccessful();
    }

    /// <summary>
    /// Вход Telegram, привязанным только что, владения не подтверждает: его мог привязать
    /// тот, у кого оказалась сессия
    /// </summary>
    [Fact]
    public async Task RecentlyLinkedLoginDoesNotConfirm()
    {
        var oleg = Oleg();
        oleg.User!.TelegramLinkedAtUtc = DateTime.UtcNow.AddHours(-1);
        var (client, _) = await TestSession.SignedInVia(oleg.Id,
            new ExternalLogin(ExternalProvider.Telegram, DateTimeOffset.UtcNow), x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Vk });

        await response.ShouldBeBadRequestWith(AuthMessages.FreshLoginRequired);
    }

    [Fact]
    public async Task StaleLoginIsNotConfirmed()
    {
        var oleg = Oleg();
        var (client, _) = await TestSession.SignedInVia(oleg.Id,
            new SessionLogin(FreshLogin.PasswordMethod, DateTimeOffset.UtcNow.AddMinutes(-11)), x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Vk });

        await response.ShouldBeBadRequestWith(AuthMessages.FreshLoginRequired);
    }

    /// <summary>«Основное» говорит клиенту, до какого времени подтверждение действует</summary>
    [Fact]
    public async Task LoginsShowFreshLoginDeadline()
    {
        var oleg = Oleg();
        var at = DateTimeOffset.UtcNow.AddMinutes(-3);
        var (client, _) = await TestSession.SignedInVia(oleg.Id,
            new SessionLogin(FreshLogin.PasswordMethod, at), x => x.Profiles.Add(oleg));

        var logins = await client.GetFromJsonAsync<AccountLoginsDto>("/api/Account/Logins");

        Assert.InRange(logins!.FreshLoginUntilUtc!.Value, at.UtcDateTime.AddMinutes(10).AddSeconds(-2),
            at.UtcDateTime.AddMinutes(10).AddSeconds(2));
        Assert.False(logins.CanSetPasswordWithoutCurrent);
    }

    [Fact]
    public async Task StaleSessionShowsNoDeadline()
    {
        var oleg = Oleg();
        var (client, _) = await StaleSession(oleg);

        var logins = await client.GetFromJsonAsync<AccountLoginsDto>("/api/Account/Logins");

        Assert.Null(logins!.FreshLoginUntilUtc);
    }

    /// <summary>
    /// Смена логина перевыпускает токен со старой отметкой входа: профиль сохраняет логин
    /// перед почтой, и окно не должно ни теряться, ни продлеваться
    /// </summary>
    [Fact]
    public async Task ChangeUserNameKeepsLoginMark()
    {
        var oleg = Oleg();
        var at = DateTimeOffset.UtcNow.AddMinutes(-3);
        var (client, _) = await TestSession.SignedInVia(oleg.Id,
            new SessionLogin(FreshLogin.PasswordMethod, at), x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Account/ChangeUserName", new ChangeUserNameDto { UserName = "Olezhka" });

        await response.ShouldBeSuccessful();
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(session!.Token);
        Assert.Equal(at.ToUnixTimeSeconds().ToString(), token.Claims.Single(x => x.Type == FreshLogin.AuthTimeClaim).Value);
        Assert.Equal(FreshLogin.PasswordMethod, token.Claims.Single(x => x.Type == FreshLogin.MethodClaim).Value);
    }

    /// <summary>Отвязка снимает и время привязки: привязанный заново вход снова «только что»</summary>
    [Fact]
    public async Task UnlinkForgetsLinkTime()
    {
        var oleg = Oleg();
        oleg.User!.VkLinkedAtUtc = DateTime.UtcNow.AddDays(-3);
        var (token, _) = new UserBuilder("Oleg", oleg.Id).Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        await (await client.PostAsJsonAsync("/api/Account/Unlink", new UnlinkDto { Provider = ExternalProvider.Vk }))
            .ShouldBeSuccessful();

        Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).VkLinkedAtUtc);
    }

    private static async Task UseSession(HttpClient client, HttpResponseMessage response)
    {
        await response.ShouldBeSuccessful();
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
    }
}
