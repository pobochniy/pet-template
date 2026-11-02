using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Auth;

/// <summary>
/// Вход из mini-app Telegram — <c>Auth/TelegramAuth</c> по подписанному <c>initData</c> и развилка
/// первого запуска (спец. accounts, 5.6). Тестовый хост — Development, где подпись не
/// проверяется; боевой путь подписи и свежести — <see cref="MiniAppLaunchUnitTests"/>
/// </summary>
public class TelegramAuthApiTests
{
    private const long TestUserId = 12345678;
    private const string TestUsername = "testuser";

    private static string InitData(long userId = TestUserId, string? username = TestUsername, bool invitation = false)
    {
        var builder = new TelegramInitDataBuilder()
            .WithUserId(userId)
            .WithUsername(username)
            .WithBotToken(TestSettings.DefaultTelegramBotToken);
        return (invitation ? builder.FromInvitation() : builder).Please();
    }

    private static ExternalProofDto Proof(long userId = TestUserId, string? username = TestUsername) => new()
    {
        Provider = ExternalProvider.Telegram,
        InitData = InitData(userId, username)
    };

    /// <summary>Открыт по приглашению — развилки нет, аккаунт заводится сразу</summary>
    [Fact]
    public async Task CanCreateUserFromInvitation()
    {
        var (client, db) = await Given.ApiClient();
        var before = DateTime.UtcNow;

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramAuth",
            new TelegramAuthDto { InitData = InitData(invitation: true) });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.False(string.IsNullOrWhiteSpace(result!.Token), "Token should not be empty");
        Assert.Equal(TestUsername, result.Profile.UserName);
        Assert.True(result.Profile.UserNameIsGenerated);
        // Новый аккаунт — без пространств: их заводит человек сам (спец. authorization, 3)
        Assert.Empty(result.PermissionsByWorkspace);
        var user = await db.Users.AsNoTracking().SingleAsync();
        Assert.InRange(user.CreatedAtUtc, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task CanAuthUser()
    {
        const long existingUserId = 87654321;
        var workspace = new WorkspaceBuilder().Please();
        var (_, profile) = new UserBuilder("Alexey", telegramId: existingUserId)
            .WithPermissions(workspace.Id, PermissionEnum.PermissionManagement)
            .Please();
        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(profile);
        });

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramAuth",
            new TelegramAuthDto { InitData = InitData(existingUserId, "DifferentUsername") });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.False(result!.IsNewAccount);
        // Логин — из базы, а не из Telegram
        Assert.Equal("Alexey", result.Profile.UserName);
        Assert.Contains(PermissionEnum.PermissionManagement, result.PermissionsByWorkspace[workspace.Id]);
    }

    /// <summary>
    /// <c>@username</c> занят (без учёта регистра) — логин <c>tg&lt;id&gt;</c>, а не
    /// <c>@username</c> со случайным хвостом (спец. accounts, 4.2)
    /// </summary>
    [Fact]
    public async Task FallsBackToTelegramIdWhenUsernameTaken()
    {
        var (_, taken) = new UserBuilder("TestUser").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(taken));

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramAuth",
            new TelegramAuthDto { InitData = InitData(invitation: true) });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal($"tg{TestUserId}", result!.Profile.UserName);
        Assert.True(result.Profile.UserNameIsGenerated);
    }

    /// <summary>
    /// Неизвестный Telegram без приглашения — развилка, а не молчаливое заведение аккаунта
    /// (спец. accounts, 5.6): 404 с кодом, и аккаунта нет
    /// </summary>
    [Fact]
    public async Task UnknownTelegramWithoutInvitationCreatesNothing()
    {
        var (client, db) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramAuth", new TelegramAuthDto { InitData = InitData() });

        await response.ShouldBeNotFoundWith(AuthMessages.UnknownLogin);
        Assert.Empty(db.Profiles);
    }

    /// <summary>«Начать» на развилке — тот же подписанный вход заводит аккаунт</summary>
    [Fact]
    public async Task RegisterExternalCreatesAccount()
    {
        var (client, db) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/RegisterExternal",
            new RegisterExternalDto { Proof = Proof(), Language = LanguageConstants.English });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.True(result!.IsNewAccount);
        Assert.Equal(TestUsername, result.Profile.UserName);
        Assert.Equal(LanguageConstants.English, (await db.Profiles.AsNoTracking().SingleAsync()).Language);
    }

    /// <summary>«Начать» второй раз тем же входом — вход в уже заведённый, а не второй аккаунт</summary>
    [Fact]
    public async Task RegisterExternalWithKnownTelegramSignsIn()
    {
        var (_, oleg) = new UserBuilder("Oleg", telegramId: TestUserId).Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/RegisterExternal", new RegisterExternalDto { Proof = Proof() });

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.False(result!.IsNewAccount);
        Assert.Equal(oleg.Id, result.Profile.Id);
        Assert.Equal(1, await db.Profiles.CountAsync());
    }

    /// <summary>
    /// «У меня уже есть аккаунт» — вход логином и паролем привязывает этот Telegram,
    /// и следующий запуск mini-app входит сразу
    /// </summary>
    [Fact]
    public async Task LogInAndLinkAttachesTelegram()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/LogInAndLink", new LogInAndLinkDto
        {
            Login = "Oleg",
            Password = UserBuilder.SuperAdminPassword,
            Proof = Proof()
        });

        await response.ShouldBeSuccessful();
        Assert.Equal(TestUserId, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramId);
    }

    /// <summary>
    /// Развилка помнит, когда Telegram привязан (8.3): сутки вход им не задаёт пароль без
    /// старого. Сессия — свежий вход паролем (8.6): владение доказал пароль
    /// </summary>
    [Fact]
    public async Task LogInAndLinkRecordsLinkTimeAndPasswordSession()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/LogInAndLink", new LogInAndLinkDto
        {
            Login = "Oleg",
            Password = UserBuilder.SuperAdminPassword,
            Proof = Proof()
        });

        await response.ShouldBeSuccessful();
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(session!.Token);
        Assert.Equal(FreshLogin.PasswordMethod, token.Claims.Single(x => x.Type == FreshLogin.MethodClaim).Value);
        Assert.NotNull((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramLinkedAtUtc);
    }

    [Fact]
    public async Task LogInAndLinkRejectsWrongPassword()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/LogInAndLink", new LogInAndLinkDto
        {
            Login = "Oleg",
            Password = "не тот",
            Proof = Proof()
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramId);
    }

    /// <summary>
    /// «У меня уже есть аккаунт», а этот Telegram уже вошёл в другой аккаунт: занятый вход
    /// ведёт в слияние (разделы 3, 7.1) — сессия аккаунта по паролю и просмотр слияния,
    /// привязки нет
    /// </summary>
    [Fact]
    public async Task LogInAndLinkOffersMergeWhenTelegramBelongsToAnother()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (_, masha) = new UserBuilder("Masha", telegramId: TestUserId).WithoutPassword().Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.AddRange(oleg, masha));

        var response = await client.PostAsJsonAsync("/api/Auth/LogInAndLink", new LogInAndLinkDto
        {
            Login = "Oleg",
            Password = UserBuilder.SuperAdminPassword,
            Proof = Proof()
        });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal(oleg.Id, result!.Profile.Id);
        Assert.Equal(masha.Id, result.MergePreview!.Other.UserId);
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).TelegramId);
    }

    /// <summary><c>Auth/GenerateTestInitData</c> — отладочная выдача подписи — удалён</summary>
    [Fact]
    public async Task TestInitDataGeneratorIsGone()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.GetAsync("/api/Auth/GenerateTestInitData");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
