using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Вход в админку и граница между схемами (спец. admin, 3.3, 3.5 и 6): свой токен,
/// роли клеймами, <c>404</c> на всякий отказ и лимит попыток
/// </summary>
public class AdminAuthApiTests
{
    [Fact]
    public async Task CanLogInWithAdminRole()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg));

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg"));

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AdminLoginResultDto>();
        Assert.Equal(oleg.Id, result!.Admin.UserId);
        Assert.Equal([AdminRole.AdminManagement], result.Admin.Roles);
    }

    /// <summary>Час жизни, без продления (3.3)</summary>
    [Fact]
    public async Task IssuesTokenForOneHour()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg));
        var before = DateTime.UtcNow;

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg"));

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AdminLoginResultDto>();
        Assert.InRange(result!.ExpiresUtc, before.AddHours(1), DateTime.UtcNow.AddHours(1));
    }

    /// <summary>Пустой набор ролей — это не админ (3.1), и пароль от продукта его не пускает</summary>
    [Fact]
    public async Task ReturnsNotFoundOnLoginWithoutRoles()
    {
        // Arrange
        var (_, vasya) = new UserBuilder("Vasya").Please();
        var client = await Client(db => db.Profiles.Add(vasya));

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login("Vasya"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsNotFoundOnWrongPassword()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg));

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg", "wrong-password"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Битое тело отвечает тем же <c>404</c>, а не <c>422</c> валидации: иначе ответ
    /// сообщал бы, что вход тут есть (3.5)
    /// </summary>
    [Fact]
    public async Task ReturnsNotFoundOnEmptyLogin()
    {
        // Arrange
        var client = await Client();

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", new { });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Исчерпанный лимит по учётке не пускает и с верным паролем (3.3)</summary>
    [Fact]
    public async Task LocksLoginAfterTooManyFailures()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg), attempts: 2);
        await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg", "wrong-password"));
        await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg", "wrong-password"));

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Неудачи входа в продукт — в тот же счёт (спец. accounts, 8.1): пароль у админа тот же,
    /// и подбор через <c>Auth/LogIn</c> иначе обходил бы лимит админки
    /// </summary>
    [Fact]
    public async Task ProductFailuresLockAdminLogin()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg), attempts: 2);
        await client.PostAsJsonAsync("/api/Auth/LogIn", Login("Oleg", "wrong-password"));
        await client.PostAsJsonAsync("/api/Auth/LogIn", Login("Oleg", "wrong-password"));

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Лимит считает учётку, а не строку логина (3.3): ник, почта и телефон одного
    /// человека — один владелец, иначе на подбор его пароля попыток было бы втрое больше
    /// </summary>
    [Fact]
    public async Task NickEmailAndPhoneHaveOneOwner()
    {
        // Arrange
        var (_, oleg) = new UserBuilder("Oleg").WithContacts("oleg@roga-i-kopyta.ru", "+79091112244").Please();
        var (_, services) = await new ApiApplicationFactory<Program>().SetupApplication(
            dbArrange: x => x.Profiles.Add(oleg),
            dbName: Guid.NewGuid().ToString(),
            configureSettings: TestSettings.ConfigureAuth());
        using var scope = services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<AdminAccessService>();

        // Act
        var owners = new[]
        {
            await access.OwnerOf(Login("Oleg")),
            await access.OwnerOf(Login("oleg@roga-i-kopyta.ru")),
            await access.OwnerOf(Login("+79091112244"))
        };

        // Assert
        Assert.All(owners, x => Assert.Equal(oleg.Id, x));
    }

    /// <summary>Удачный вход ничего не расходует: лимит считает только неудачи</summary>
    [Fact]
    public async Task DoesNotCountSuccessfulLogins()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg), attempts: 2);
        await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg"));
        await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg"));

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg"));

        // Assert
        await response.ShouldBeSuccessful();
    }

    /// <summary>«Кто я» — по токену: имя и роли из клеймов</summary>
    [Fact]
    public async Task CanSeeOwnRoles()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg));
        await LogInAsAdmin(client, "Oleg");

        // Act
        var response = await client.GetAsync("/api/Admin/Me");

        // Assert
        await response.ShouldBeSuccessful();
        var me = await response.Content.ReadFromJsonAsync<AdminDto>();
        Assert.Equal(oleg.Id, me!.UserId);
        Assert.Equal("Oleg", me.UserName);
        Assert.Equal([AdminRole.AdminManagement], me.Roles);
    }

    [Fact]
    public async Task ReturnsNotFoundToAnonymous()
    {
        // Arrange
        var client = await Client();

        // Act
        var response = await client.GetAsync("/api/Admin/Me");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Токен продукта админку не открывает — даже у того, у кого есть админские роли:
    /// роли едут только в токене своей схемы
    /// </summary>
    [Fact]
    public async Task ReturnsNotFoundToProductToken()
    {
        // Arrange
        var (token, oleg) = new UserBuilder("Oleg").WithAdminRoles(AdminRole.AdminManagement).Please();
        var (client, _) = await Given.ApiClient(db => db.Profiles.Add(oleg), token);

        // Act
        var response = await client.GetAsync("/api/Admin/Me");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Админский токен продукт не принимает: для продукта это просто «сессии нет»
    /// </summary>
    [Fact]
    public async Task ProductRejectsAdminToken()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg));
        await LogInAsAdmin(client, "Oleg");

        // Act
        var response = await client.GetAsync("/api/Account/Logins");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Выход — <c>200</c>: токен клиент забывает сам, а сервер гасит куку шлюза
    /// (3.3, <see cref="AdminGatewayApiTests"/>)
    /// </summary>
    [Fact]
    public async Task CanLogOut()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg));
        await LogInAsAdmin(client, "Oleg");

        // Act
        var response = await client.PostAsync("/api/Admin/Logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>«Сводка» открыта всякому, кто вошёл (3.2, 6)</summary>
    [Fact]
    public async Task CanSeeSummary()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var client = await Client(db => db.Profiles.Add(oleg));
        await LogInAsAdmin(client, "Oleg");
        var before = DateTime.UtcNow;

        // Act
        var response = await client.GetAsync("/api/Admin/Summary");

        // Assert
        await response.ShouldBeSuccessful();
        var summary = await response.Content.ReadFromJsonAsync<AdminSummaryDto>();
        Assert.False(string.IsNullOrEmpty(summary!.Version));
        Assert.InRange(summary.ServerTimeUtc, before, DateTime.UtcNow);
    }

    /// <summary>
    /// Ключ админки, совпавший с ключом продукта, — это граница на одной строке
    /// проверки аудитории. Такая конфигурация не стартует (7)
    /// </summary>
    [Fact]
    public async Task RefusesToStartWithProductSigningKey()
    {
        // Act
        var start = () => Client(settings: new Dictionary<string, string?>
        {
            ["AdminOptions:SigningKey"] = "TestSigningKeyThatIsLongEnough123456"
        });

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(start);
    }

    /// <summary>Аудитория — второе из двух значений, на которых держится граница схем</summary>
    [Fact]
    public async Task RefusesToStartWithProductAudience()
    {
        // Act
        var start = () => Client(settings: new Dictionary<string, string?>
        {
            ["AdminOptions:Audience"] = "TestAudience"
        });

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(start);
    }

    /// <summary>Пустым ключом не подписать — узнать об этом лучше на старте, чем первым входом</summary>
    [Fact]
    public async Task RefusesToStartWithoutSigningKey()
    {
        // Act
        var start = () => Client(settings: new Dictionary<string, string?>
        {
            ["AdminOptions:SigningKey"] = ""
        });

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(start);
    }

    /// <summary>Короткий ключ HS256 подбирается проще — не стартуем</summary>
    [Fact]
    public async Task RefusesToStartWithShortSigningKey()
    {
        // Act
        var start = () => Client(settings: new Dictionary<string, string?>
        {
            ["AdminOptions:SigningKey"] = "too-short-admin-key"
        });

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(start);
    }

    /// <summary>Ключ продукта — то же правило: короткий не стартует, а не роняет первый вход</summary>
    [Fact]
    public async Task RefusesToStartWithShortProductSigningKey()
    {
        // Act
        var start = () => Client(settings: new Dictionary<string, string?>
        {
            ["JwtOptions:SigningKey"] = "too-short-product-key"
        });

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(start);
    }

    private static async Task<HttpClient> Client(
        Action<ApplicationContext>? arrange = null,
        int? attempts = null,
        Dictionary<string, string?>? settings = null)
    {
        settings ??= [];
        if (attempts != null)
        {
            settings["AdminOptions:AdminLoginAttempts"] = attempts.ToString();
        }

        var (client, _) = await Given.ApiClient(arrange, settings: settings);

        return client;
    }

    private static async Task LogInAsAdmin(HttpClient client, string login)
    {
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login(login));
        await response.ShouldBeSuccessful();
        client.UseAdminSession((await response.Content.ReadFromJsonAsync<AdminLoginResultDto>())!);
    }

    private static LoginDto Login(string login, string? password = null) =>
        new() { Login = login, Password = password ?? UserBuilder.SuperAdminPassword };
}
