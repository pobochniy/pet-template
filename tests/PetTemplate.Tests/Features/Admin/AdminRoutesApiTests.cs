using System.Net;
using System.Net.Http.Json;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Каждый админский маршрут целиком (спец. admin, 3.5): без нужной роли и с токеном
/// продукта — <c>404</c>, с нужной ролью — пускает. Какая роль у какого контроллера,
/// сверяет <see cref="AdminWiringTests"/>; здесь — что проверка и правда стоит на пути
/// запроса. Чем кончится сама операция, здесь не важно: <c>400</c> на правило сервиса —
/// тоже «пустили». Новый админский маршрут дописывается в <see cref="Routes"/>, тело запроса — в <see cref="Request"/>.
///
/// Маршруты ASP.NET к регистру безразличны: <c>/api/admin/login</c> — тот же вход,
/// что <c>/api/Admin/Login</c>. На этом держится замок nginx на основном домене
/// (спец. admin, 7): <c>location ~* ^/api/admin(/|$)</c> — регулярка без учёта
/// регистра. Начни API различать регистр — и замок с тестом разойдутся
/// </summary>
public class AdminRoutesApiTests
{
    /// <summary>Все роли, какие есть, — у Олега в базе</summary>
    private static readonly AdminRole[] AllRoles = AdminRoleSet.All;

    /// <summary>
    /// Id в маршрутах — постоянные, а не случайные: данные теорий xunit собирает при
    /// обнаружении, и в прогоне, где обнаружение и запуск — разные процессы, случайный id
    /// маршрута разошёлся бы с id в базе
    /// </summary>
    private static readonly Guid VasyaId = new("0197a000-0000-7000-8000-000000000016");

    private static readonly Guid InvitationId = new("0197a000-0000-7000-8000-000000000116");

    /// <summary>Маршрут и роль, которая его открывает; <c>None</c> — любая</summary>
    public static TheoryData<string, string, AdminRole> Routes() => new()
    {
        { "GET", "/api/Admin/Me", AdminRole.None },
        { "POST", "/api/Admin/Logout", AdminRole.None },
        { "GET", "/api/Admin/Summary", AdminRole.None },
        { "GET", "/api/Admin/Accounts?query=Vasya", AdminRole.Accounts },
        { "GET", "/api/Admin/Accounts/Journal", AdminRole.Accounts },
        { "GET", $"/api/Admin/Accounts/{VasyaId}", AdminRole.Accounts },
        { "POST", $"/api/Admin/Accounts/{VasyaId}/Delete", AdminRole.Accounts },
        { "POST", $"/api/Admin/Accounts/{VasyaId}/IssueReset", AdminRole.Accounts },
        { "POST", $"/api/Admin/Accounts/{VasyaId}/ReleaseEmail", AdminRole.Accounts },
        { "POST", $"/api/Admin/Accounts/{VasyaId}/ReleasePhone", AdminRole.Accounts },
        { "GET", "/api/Admin/Admins", AdminRole.AdminManagement },
        { "POST", $"/api/Admin/Admins/{VasyaId}/Roles/{(int)AdminRole.AdminManagement}", AdminRole.AdminManagement },
        { "DELETE", $"/api/Admin/Admins/{VasyaId}/Roles/{(int)AdminRole.Accounts}", AdminRole.AdminManagement },
        { "DELETE", $"/api/Admin/Admins/{VasyaId}", AdminRole.AdminManagement },
        { "GET", "/api/Admin/Invitations", AdminRole.AdminManagement },
        { "POST", "/api/Admin/Invitations", AdminRole.AdminManagement },
        { "DELETE", $"/api/Admin/Invitations/{InvitationId}", AdminRole.AdminManagement },
        { "POST", "/api/Admin/Search/Reindex", AdminRole.AdminManagement },
        { "POST", "/api/Admin/Models", AdminRole.Models },
        { "PUT", "/api/Admin/Models/Order", AdminRole.Models },
        { "POST", $"/api/Admin/Models/{InvitationId}/Check", AdminRole.Models },
        { "GET", "/api/Admin/Dialogs", AdminRole.Dialogs },
        { "GET", "/api/Admin/Dialogs/Evaluations", AdminRole.Dialogs },
        { "POST", "/api/Admin/Dialogs/Evaluations", AdminRole.Dialogs },
        { "GET", "/api/Admin/AccessLog", AdminRole.AdminManagement }
    };

    /// <summary>Те же маршруты без ролей — для проверок, которым роль не важна</summary>
    public static TheoryData<string, string> Paths()
    {
        var paths = new TheoryData<string, string>();
        foreach (var row in Routes()) paths.Add(row.Data.Item1, row.Data.Item2);
        return paths;
    }

    /// <summary>Контроль: с нужной ролью маршрут пускает — иначе отказы ниже ничего не значат</summary>
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OpensWithRequiredRole(string method, string url, AdminRole role)
    {
        // Arrange
        var client = await AdminClient(role == AdminRole.None ? [AdminRole.AdminManagement] : [role]);

        // Act
        var response = await client.SendAsync(Request(method, url));

        // Assert
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Все роли, кроме нужной, — <c>404</c>. Маршруту «любой роли» не хватает токена
    /// без единой роли: это не админ (3.1)
    /// </summary>
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ReturnsNotFoundWithoutRequiredRole(string method, string url, AdminRole role)
    {
        // Arrange
        var client = await AdminClient(AllRoles.Where(x => role != AdminRole.None && x != role).ToArray());

        // Act
        var response = await client.SendAsync(Request(method, url));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Токен продукта не открывает ни один маршрут — даже тому, у кого в базе все роли</summary>
    [Theory]
    [MemberData(nameof(Paths))]
    public async Task ReturnsNotFoundToProductToken(string method, string url)
    {
        // Arrange
        var (token, oleg) = new UserBuilder("Oleg").WithAdminRoles(AllRoles).Please();
        var (client, _) = await Given.ApiClient(db => Arrange(db, oleg), token);

        // Act
        var response = await client.SendAsync(Request(method, url));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/Admin/Login")]
    [InlineData("/api/admin/login")]
    [InlineData("/API/ADMIN/LOGIN")]
    public async Task LogsInInAnyCase(string url)
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var (client, _) = await Given.ApiClient(db => db.Profiles.Add(oleg));

        // Act
        var response = await client.PostAsJsonAsync(url, new LoginDto
        {
            Login = "Oleg",
            Password = UserBuilder.SuperAdminPassword
        });

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AdminLoginResultDto>();
        Assert.Equal(oleg.Id, result!.Admin.UserId);
    }

    /// <summary>
    /// Отказ на админском пути неотличим от несуществующего пути (спец. admin, 3.5): чужой
    /// метод (было бы <c>405</c>), не-JSON тело (<c>415</c>), запрос без токена (<c>404</c>
    /// с ProblemDetails) — пустой <c>404</c>, как у <c>/api/Admin/Nothing</c>
    /// </summary>
    [Fact]
    public async Task RefusalsLookLikeMissingPath()
    {
        // Arrange
        var (client, _) = await Given.ApiClient();
        var missing = await client.GetAsync("/api/Admin/Nothing");

        // Act
        var refusals = new[]
        {
            await client.GetAsync("/api/Admin/Login"),
            await client.PostAsync("/api/Admin/Login", new StringContent("Oleg", System.Text.Encoding.UTF8, "text/plain")),
            await client.GetAsync("/api/Admin/Me"),
            await client.PostAsync("/api/admin/me", null)
        };

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var response in refusals)
        {
            Assert.Equal(missing.StatusCode, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Empty(response.Content.Headers.Allow);
        }
    }

    /// <summary>Запрос с телом, которое примет сервис, — у методов, которым оно нужно</summary>
    private static HttpRequestMessage Request(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        object? body = (method, url) switch
        {
            ("POST", "/api/Admin/Invitations") => new AdminInvitationCreateDto { Roles = [AdminRole.Accounts] },
            ("POST", "/api/Admin/Models") => new AdminModelEditDto { Provider = "local", Key = "qwen3", DisplayName = "Своя" },
            ("PUT", "/api/Admin/Models/Order") => Array.Empty<Guid>(),
            ("POST", "/api/Admin/Dialogs/Evaluations") => new AdminEvaluationStartDto { ModelId = InvitationId, BatchSize = 1 },
            _ when url.EndsWith("/Delete") || url.EndsWith("/IssueReset") =>
                new AdminAccountReasonDto { Reason = "просьба в поддержку" },
            _ when url.EndsWith("/ReleaseEmail") =>
                new AdminReleaseEmailDto { Email = "vasya@example.com", Reason = "ящик чужой" },
            _ when url.EndsWith("/ReleasePhone") =>
                new AdminReleasePhoneDto { Phone = "+79990000001", Reason = "номер чужой" },
            _ => null
        };

        if (body != null) request.Content = JsonContent.Create(body, body.GetType());

        return request;
    }

    /// <summary>
    /// У Олега в базе все роли: что пустит, решает токен — проверка урезает его роли
    /// до тех, что в базе, а там есть все
    /// </summary>
    private static async Task<HttpClient> AdminClient(AdminRole[] roles)
    {
        var oleg = Given.SystemAdmin(AllRoles);
        var (client, _) = await Given.AdminClient(oleg.Id, roles, db => Arrange(db, oleg));

        return client;
    }

    /// <summary>
    /// Вася — админ с одной ролью: на нём пробуются карточка, сброс, почта и удаление
    /// аккаунта, выдача, снятие и удаление админа. Приглашение — под отзыв
    /// </summary>
    private static void Arrange(ApplicationContext db, PetTemplate.Db.Entity.Profile oleg)
    {
        db.Profiles.AddRange(oleg, new UserBuilder("Vasya", id: VasyaId)
            .WithContacts("vasya@example.com", "+79990000031")
            .WithAdminRoles(AdminRole.Accounts)
            .Please().profile);
        var now = DateTime.UtcNow;
        db.AdminInvitations.Add(new AdminInvitation
        {
            Id = InvitationId,
            TokenHash = AdminInvitation.HashOf(Guid.NewGuid()),
            Roles = AdminRole.Accounts,
            Status = AdminInvitationStatus.Pending,
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(3),
            CreatedByUserId = oleg.Id
        });
    }
}
