using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Auth;

/// <summary>
/// Вход и регистрация через Telegram на сайте — <c>Auth/TelegramLogin</c>, <c>id_token</c> окна
/// «Log In with Telegram» (спец. accounts, 5.2). Подпись проверяется копией ключей, без сети:
/// в тестах ключ свой (<see cref="TestSettings.TelegramLoginKey"/>)
/// </summary>
public class TelegramLoginApiTests
{
    private const long TelegramId = 12345678;

    private static Task<(HttpClient client, ApplicationContext db)> Client(Action<ApplicationContext>? arrange = null) =>
        Given.ApiClient(arrange);

    private static ExternalCodeDto Code(string? idToken = null, bool existingOnly = false,
        string state = TelegramLoginBuilder.DefaultNonce) =>
        new()
        {
            Code = idToken ?? new TelegramLoginBuilder().WithUserId(TelegramId).Build(),
            State = state,
            ExistingOnly = existingOnly
        };

    /// <summary>Неизвестный Telegram заводит аккаунт: логин — <c>@username</c>, если подходит (4.2)</summary>
    [Fact]
    public async Task UnknownTelegramCreatesAccount()
    {
        var (client, _) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code());

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.True(result!.IsNewAccount);
        Assert.Equal("masha", result.Profile.UserName);
        // Новый аккаунт — без пространств: их заводит человек сам (спец. authorization, 3)
        Assert.Empty(result.PermissionsByWorkspace);
    }

    /// <summary>Заведённый входом аккаунт — без пароля, с автоматическим логином и привязанным Telegram</summary>
    [Fact]
    public async Task CreatedAccountHasNoPasswordAndGeneratedUserName()
    {
        var (client, db) = await Client();

        await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code());

        var profile = await db.Profiles.Include(x => x.User).AsNoTracking().SingleAsync();
        Assert.Null(profile.User!.PasswordHash);
        Assert.True(profile.UserNameIsGenerated);
        Assert.Equal(TelegramId, profile.User.TelegramId);
        Assert.NotNull(profile.User.LastLoginAtUtc);
    }

    /// <summary><c>@username</c> не годится как логин — <c>tg&lt;id&gt;</c> (4.2)</summary>
    [Theory]
    [InlineData("tg42")]
    [InlineData("ab")]
    public async Task UnsuitableUsernameFallsBackToTelegramId(string username)
    {
        var (client, _) = await Client();
        var token = new TelegramLoginBuilder().WithUserId(TelegramId).WithUsername(username).Build();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(token));

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal($"tg{TelegramId}", result!.Profile.UserName);
    }

    /// <summary>
    /// <c>@username</c> занят (без учёта регистра) — логин <c>tg&lt;id&gt;</c>, а не
    /// <c>@username</c> со случайным хвостом (4.2)
    /// </summary>
    [Fact]
    public async Task TakenUsernameFallsBackToTelegramId()
    {
        var (_, taken) = new UserBuilder("Masha").Please();
        var (client, _) = await Client(x => x.Profiles.Add(taken));

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code());

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal($"tg{TelegramId}", result!.Profile.UserName);
        Assert.True(result.Profile.UserNameIsGenerated);
    }

    /// <summary>Один человек — один аккаунт, в который ведёт любой вход (раздел 1)</summary>
    [Fact]
    public async Task KnownTelegramSignsIntoExistingAccount()
    {
        var workspace = new WorkspaceBuilder().Please();
        var (_, oleg) = new UserBuilder("Oleg").WithTelegram(TelegramId)
            .WithPermissions(workspace.Id, PetTemplate.Shared.Enums.PermissionEnum.PermissionManagement)
            .Please();
        var (client, _) = await Client(x =>
        {
            x.Workspaces.Add(workspace);
            x.Profiles.Add(oleg);
        });

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code());

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.False(result!.IsNewAccount);
        Assert.Equal(oleg.Id, result.Profile.Id);
        // Логин — из базы, а не из Telegram
        Assert.Equal("Oleg", result.Profile.UserName);
        Assert.Contains(PetTemplate.Shared.Enums.PermissionEnum.PermissionManagement, result.PermissionsByWorkspace[workspace.Id]);
    }

    /// <summary>Вход через площадку — тоже вход: дата для карточки поддержки (8.5)</summary>
    [Fact]
    public async Task KnownTelegramStampsLastLogin()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithTelegram(TelegramId).Please();
        var (client, db) = await Client(x => x.Profiles.Add(oleg));

        await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code());

        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.NotNull(user.LastLoginAtUtc);
    }

    /// <summary>
    /// Токен одноразовый: подсмотренный не даёт второй сессии, хотя срок приёма у него ещё
    /// не вышел (5.2)
    /// </summary>
    [Fact]
    public async Task RejectsReplayedToken()
    {
        var (client, _) = await Client();
        var token = new TelegramLoginBuilder().WithUserId(TelegramId).Build();
        await (await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(token))).ShouldBeSuccessful();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(token));

        await response.ShouldBeBadRequestWith(AuthMessages.ExternalFailed);
    }

    [Fact]
    public async Task RejectsForeignSignature()
    {
        var (client, db) = await Client();
        using var foreignKey = RSA.Create(2048);
        var forged = new TelegramLoginBuilder().WithKey(foreignKey).Build();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(forged));

        await response.ShouldBeBadRequestWith(AuthMessages.ExternalFailed);
        Assert.Empty(db.Profiles);
    }

    /// <summary>Вход, выданный другому боту, — не вход на этот сайт</summary>
    [Fact]
    public async Task RejectsTokenForAnotherBot()
    {
        var (client, db) = await Client();
        var foreign = new TelegramLoginBuilder().WithAudience("999999").Build();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(foreign));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(db.Profiles);
    }

    /// <summary>
    /// Токен из чужого браузера: его <c>nonce</c> — чужой <c>state</c>. Подсунуть человеку
    /// свой вход, чтобы он оказался в чужом аккаунте, так нельзя
    /// </summary>
    [Fact]
    public async Task RejectsTokenFromAnotherLoginAttempt()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(state: "another-state"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(db.Profiles);
    }

    /// <summary>Подсмотренный однажды вход не остаётся входом надолго (<c>TelegramLoginMaxAge</c>)</summary>
    [Fact]
    public async Task RejectsStaleSignature()
    {
        var (client, db) = await Client();
        var stale = new TelegramLoginBuilder().WithIssuedAt(DateTimeOffset.UtcNow.AddMinutes(-11)).Build();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(stale));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(db.Profiles);
    }

    [Theory]
    [InlineData("not a jwt at all")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJpZCI6MTIzNDU2Nzh9.")] // без подписи
    public async Task RejectsMalformedData(string idToken)
    {
        var (client, _) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(idToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Клиенту уходят только публичные параметры: id бота и адрес возврата</summary>
    [Fact]
    public async Task ConfigCarriesBotId()
    {
        var (client, _) = await Client();

        var config = await client.GetFromJsonAsync<ExternalLoginsConfigDto>("/api/Auth/ExternalLogins");

        Assert.Equal(TestSettings.TelegramBotId, config!.TelegramBotId);
        Assert.Equal("https://example.com/auth/telegram", config.TelegramRedirectUri);
    }

    /// <summary>Id бота не задан — площадка выключена: кнопки нет, токены не принимаются</summary>
    [Fact]
    public async Task DisabledTelegramRefusesLogin()
    {
        var (client, db) = await Given.ApiClient(
            settings: new Dictionary<string, string?> { ["TelegramLoginOptions:BotId"] = "" });

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(db.Profiles);
    }

    /// <summary>
    /// Восстановление доступа через привязанный вход (8.2, 8.3): неизвестный Telegram аккаунт
    /// не заводит — человек пришёл за своим, и пустой новый ему не поможет
    /// </summary>
    [Fact]
    public async Task RecoveryWithUnknownTelegramCreatesNoAccount()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(existingOnly: true));

        await response.ShouldBeNotFoundWith(AuthMessages.LoginNotLinked);
        Assert.Empty(db.Profiles);
    }

    [Fact]
    public async Task RecoveryWithLinkedTelegramSignsIn()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithTelegram(TelegramId).Please();
        var (client, _) = await Client(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", Code(existingOnly: true));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal(oleg.Id, result!.Profile.Id);
    }

    /// <summary>Язык браузера — языком нового профиля: на нём уйдут письма (8.4)</summary>
    [Fact]
    public async Task NewAccountTakesBrowserLanguage()
    {
        var (client, db) = await Client();
        var code = Code();
        code.Language = "en";

        await client.PostAsJsonAsync("/api/Auth/TelegramLogin", code);

        Assert.Equal("en", (await db.Profiles.AsNoTracking().SingleAsync()).Language);
    }
}
