using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Шлюз к Grafana (спец. admin, 3.3): кука при входе и <c>Admin/Auth/Verify</c>, которым
/// nginx спрашивает, пускать ли. Кука только подтверждает личность — ни один админский
/// метод по ней не открывается.
///
/// Куку тест шлёт сам, как nginx пересылает в подзапрос то, что прислал браузер:
/// хранилище клиента тестов выключено (<c>SetupApplication</c>)
/// </summary>
public class AdminGatewayApiTests
{
    private const string Cookie = "pettemplate_admin";
    private const string Domain = ".example.com";

    /// <summary>Атрибуты куки названы явно (3.3): она на весь проект одна</summary>
    [Fact]
    public async Task LoginIssuesGatewayCookie()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login());

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AdminLoginResultDto>();
        var cookie = GatewayCookie(response);
        Assert.Equal(result!.Token, cookie.Value.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal(Domain, cookie.Domain.ToString());
        Assert.Equal("/", cookie.Path.ToString());
        Assert.InRange(cookie.Expires!.Value - result.ExpiresUtc, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task VerifyAnswersWithLoginAndRoles()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts, AdminRole.AdminManagement);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));
        var cookie = await LogIn(client);

        // Act
        var response = await client.SendAsync(Verify(cookie));

        // Assert
        await response.ShouldBeSuccessful();
        Assert.Equal(oleg.Id.ToString(), response.Headers.GetValues("X-Admin-Login").Single());
        Assert.Equal("AdminManagement,Accounts", response.Headers.GetValues("X-Admin-Roles").Single());
    }

    /// <summary>
    /// Роль, снятую после входа, шлюз уже не называет (3.3): токен урезается до ролей
    /// в базе и в куке, как в заголовке
    /// </summary>
    [Fact]
    public async Task VerifyDropsRevokedRole()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts, AdminRole.AdminManagement);
        var (client, db) = await Client(x => x.Profiles.Add(oleg));
        var cookie = await LogIn(client);
        db.UserInAdminRoles.RemoveRange(db.UserInAdminRoles.Where(x => x.UserId == oleg.Id && x.Role == AdminRole.AdminManagement));
        await db.SaveChangesAsync();

        // Act
        var response = await client.SendAsync(Verify(cookie));

        // Assert
        await response.ShouldBeSuccessful();
        Assert.Equal("Accounts", response.Headers.GetValues("X-Admin-Roles").Single());
    }

    [Fact]
    public async Task VerifyRejectsRequestWithoutCookie()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));

        // Act
        var response = await client.GetAsync("/api/Admin/Auth/Verify");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Доступ кончается сам, со сроком куки (3.3), — если не вышли раньше</summary>
    [Fact]
    public async Task VerifyRejectsExpiredCookie()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));
        var expired = new AdminTokenFactory(MsOptions.Create(new AdminOptions
        {
            SigningKey = TestSettings.AdminSigningKey,
            TokenLifetime = TimeSpan.FromSeconds(-5)
        })).Build(Admin(oleg, AdminRole.Accounts)).Token;

        // Act
        var response = await client.SendAsync(Verify(expired));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Токен продукта, положенный в куку, шлюз не открывает — схема та же, что у админки</summary>
    [Fact]
    public async Task VerifyRejectsProductTokenInCookie()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));
        var product = await client.PostAsJsonAsync("/api/Auth/LogIn", Login());
        await product.ShouldBeSuccessful();
        var token = (await product.Content.ReadFromJsonAsync<AuthenticationResultDto>())!.Token;

        // Act
        var response = await client.SendAsync(Verify(token));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Токен своей схемы без единой роли — не админ (3.1), и шлюз его не пускает</summary>
    [Fact]
    public async Task VerifyRejectsCookieWithoutRoles()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));
        var token = Factory().Build(Admin(oleg)).Token;

        // Act
        var response = await client.SendAsync(Verify(token));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Verify авторизуется кукой, а не заголовком: nginx шлёт то, что прислал браузер</summary>
    [Fact]
    public async Task VerifyIgnoresAuthorizationHeader()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            Factory().Build(Admin(oleg, AdminRole.Accounts)).Token);

        // Act
        var response = await client.GetAsync("/api/Admin/Auth/Verify");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Кука ездит и на основной сайт (3.3), поэтому админку она не открывает — ни
    /// на чтение, ни тем более на изменение: для этого нужен заголовок
    /// </summary>
    [Fact]
    public async Task CookieDoesNotOpenAdmin()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.AdminManagement);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));
        client.DefaultRequestHeaders.Add("Cookie", $"{Cookie}={await LogIn(client)}");

        // Act
        var me = await client.GetAsync("/api/Admin/Me");
        var invite = await client.PostAsJsonAsync("/api/Admin/Invitations",
            new AdminInvitationCreateDto { Roles = [AdminRole.Accounts] });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, me.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invite.StatusCode);
    }

    /// <summary>
    /// Выход гасит куку шлюза (3.3): клиент её, <c>HttpOnly</c>, не видит. Стирает
    /// с теми же доменом и путём, что ставил вход, — иначе браузер оставил бы прежнюю
    /// </summary>
    [Fact]
    public async Task LogoutDropsGatewayCookie()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));
        var login = await client.PostAsJsonAsync("/api/Admin/Login", Login());
        client.UseAdminSession((await login.Content.ReadFromJsonAsync<AdminLoginResultDto>())!);

        // Act
        var response = await client.PostAsync("/api/Admin/Logout", null);

        // Assert
        await response.ShouldBeSuccessful();
        var cookie = GatewayCookie(response);
        Assert.Equal(string.Empty, cookie.Value.ToString());
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
        Assert.Equal(Domain, cookie.Domain.ToString());
        Assert.Equal("/", cookie.Path.ToString());
    }

    /// <summary>
    /// Выход — только с заголовком: по одной куке чужой сайт подложенной формой
    /// разлогинил бы админа
    /// </summary>
    [Fact]
    public async Task LogoutByCookieAloneIsNotFound()
    {
        // Arrange
        var oleg = Given.SystemAdmin(AdminRole.Accounts);
        var (client, _) = await Client(db => db.Profiles.Add(oleg));
        client.DefaultRequestHeaders.Add("Cookie", $"{Cookie}={await LogIn(client)}");

        // Act
        var response = await client.PostAsync("/api/Admin/Logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains(HeaderNames.SetCookie));
    }

    /// <summary>Хост с доменом куки, как на проде (<c>appsettings.Release.json</c>)</summary>
    private static Task<(HttpClient client, ApplicationContext db)> Client(Action<ApplicationContext> arrange) =>
        Given.ApiClient(arrange, settings: new Dictionary<string, string?>
        {
            ["AdminOptions:GatewayCookieDomain"] = Domain
        });

    private static async Task<string> LogIn(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login());
        await response.ShouldBeSuccessful();
        return GatewayCookie(response).Value.ToString();
    }

    /// <summary>Выпуск токена мимо входа — с тестовым ключом, как у хоста</summary>
    private static AdminTokenFactory Factory() =>
        new(MsOptions.Create(new AdminOptions { SigningKey = TestSettings.AdminSigningKey }));

    private static AdminDto Admin(PetTemplate.Db.Entity.Profile oleg, params AdminRole[] roles) =>
        new() { UserId = oleg.Id, UserName = "Oleg", Roles = roles };

    private static SetCookieHeaderValue GatewayCookie(HttpResponseMessage response) =>
        SetCookieHeaderValue.ParseList(response.Headers.GetValues(HeaderNames.SetCookie).ToList())
            .Single(x => x.Name == Cookie);

    private static HttpRequestMessage Verify(string cookie) => new(HttpMethod.Get, "/api/Admin/Auth/Verify")
    {
        Headers = { { "Cookie", $"{Cookie}={cookie}" } }
    };

    private static LoginDto Login() => new() { Login = "Oleg", Password = UserBuilder.SuperAdminPassword };
}
