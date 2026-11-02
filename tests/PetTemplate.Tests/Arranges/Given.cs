using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Arranges;

public static class Given
{
    /// <summary>
    /// Приложение на своей базе теста. Остановит его и дропнет базу <see cref="TestHosts"/>
    /// после теста
    /// </summary>
    /// <param name="workspaceId">Активное пространство — заголовок <c>X-Workspace-Id</c></param>
    /// <param name="dbName">
    /// Общая база для нескольких клиентов одного теста (свой и чужой участник);
    /// по умолчанию — своя на каждый вызов
    /// </param>
    /// <param name="settings">Настройки поверх тестовых — площадка входа (<c>FakeVkId.Settings</c>)</param>
    /// <param name="configureServices">Подмены сервисов — HTTP-клиент VK ID (<c>FakeVkId.Services</c>)</param>
    public static async Task<(HttpClient client, ApplicationContext db)> ApiClient(
        Action<ApplicationContext>? dbArrange = null,
        string? token = null,
        Guid? workspaceId = null,
        string? dbName = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new ApiApplicationFactory<Program>();
        var configureAuth = TestSettings.ConfigureAuth();

        var (client, serviceProvider) = await factory
            .SetupApplication(
                dbArrange: dbArrange,
                configureSettings: all =>
                {
                    configureAuth(all);
                    foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
                    {
                        all[key] = value;
                    }
                },
                bearerToken: token,
                dbName: dbName ?? Guid.NewGuid().ToString(),
                configureServices: configureServices,
                workspaceId: workspaceId);
        var db = serviceProvider.GetRequiredService<ApplicationContext>();
        return (client, db);
    }

    /// <summary>
    /// Приложение с живым воркером очереди — для тестов обработки команд (письма).
    /// Очередь воркер разбирает сам, опрашивая её часто; тест ждёт, пока команда уйдёт
    /// из таблицы. <paramref name="configureServices"/> — подмены (заглушка почты)
    /// </summary>
    public static async Task<ApplicationContext> WorkerClient(
        Action<ApplicationContext>? dbArrange = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var configureAuth = TestSettings.ConfigureAuth();
        var (_, serviceProvider) = await new ApiApplicationFactory<Program>(withQueueWorker: true)
            .SetupApplication(
                dbArrange: dbArrange,
                dbName: Guid.NewGuid().ToString(),
                configureSettings: settings =>
                {
                    configureAuth(settings);
                    settings["CommandQueueOptions:PollInterval"] = "00:00:00.100";
                },
                configureServices: configureServices);
        return serviceProvider.GetRequiredService<ApplicationContext>();
    }

    /// <summary>
    /// Пространство с владельцем: у <paramref name="owner"/> все права в нём, как у создателя.
    /// Сущности кладёт в базу тест — пространство раньше прав, на него ссылается внешний ключ
    /// </summary>
    public static (Workspace workspace, string token, Profile owner) Owner(
        string workspaceName = "Рога и Копыта",
        string ownerName = "Oleg")
    {
        var workspace = new WorkspaceBuilder(workspaceName).Please();
        var (token, owner) = new UserBuilder(ownerName)
            .WithAllPermissions(workspace.Id)
            .WithPrimaryWorkspace(workspace.Id)
            .Please();

        return (workspace, token, owner);
    }

    /// <summary>
    /// Живое приглашение в пространство: выдано сейчас, не принято и не отозвано.
    /// Набор прав — то, что приглашение выдаст принявшему
    /// </summary>
    public static WorkspaceInvitation Invitation(Guid workspaceId, Guid createdBy, params PermissionEnum[] permissions)
    {
        return new WorkspaceInvitationBuilder(workspaceId, createdBy)
            .WithPermissions(permissions.Length > 0 ? permissions : [PermissionEnum.WorkspaceMemberRead])
            .Please();
    }

    /// <summary>
    /// Админ системы (спец. admin, 3.1) — Олег с ролями админки в базе; без ролей в вызове —
    /// с единственной ролью каркаса, <see cref="AdminRole.AdminManagement"/>. Пароль —
    /// <see cref="UserBuilder.SuperAdminPassword"/>: входит через <c>Admin/Login</c>
    /// </summary>
    public static Profile SystemAdmin(params AdminRole[] roles) =>
        new UserBuilder("Oleg")
            .WithAdminRoles(roles.Length == 0 ? [AdminRole.AdminManagement] : roles)
            .Please().profile;

    /// <summary>
    /// Клиент админки с токеном, выписанным напрямую (<c>AdminTokenFactory</c>), без входа.
    /// Подпись и аудитория настоящие — схема admin его принимает, — но роли при выпуске
    /// не сверяются с базой: так получаются и токен без единой роли, который вход
    /// не выдаст, и роль, снятая в базе после входа. Пустит ли она, решает проверка
    /// токена: на каждом запросе роли урезаются до тех, что в базе (спец. admin, 3.3)
    /// </summary>
    public static async Task<(HttpClient client, ApplicationContext db)> AdminClient(
        Guid userId,
        AdminRole[] tokenRoles,
        Action<ApplicationContext>? dbArrange = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var configureAuth = TestSettings.ConfigureAuth();
        var (client, services) = await new ApiApplicationFactory<Program>().SetupApplication(
            dbArrange: dbArrange,
            dbName: Guid.NewGuid().ToString(),
            configureSettings: all =>
            {
                configureAuth(all);
                foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
                {
                    all[key] = value;
                }
            });

        client.UseAdminSession(services.GetRequiredService<AdminTokenFactory>().Build(new AdminDto
        {
            UserId = userId,
            UserName = "Oleg",
            Roles = tokenRoles
        }));

        return (client, services.GetRequiredService<ApplicationContext>());
    }

    /// <summary>Строки ролей админки — по одной на роль, как их заводит владелец (спец. admin, 3.1)</summary>
    public static IEnumerable<UserInAdminRole> AdminRoles(Guid userId, params AdminRole[] roles) =>
        roles.Select(role => new UserInAdminRole
        {
            UserId = userId,
            Role = role,
            GrantedUtc = DateTime.UtcNow
        });

    /// <summary>
    /// Приложение без клиента — для тестов, которые зовут сервис сам (чистка, гонки): своя
    /// база, сервисы берутся из областей провайдера
    /// </summary>
    public static async Task<IServiceProvider> Services(Action<ApplicationContext>? dbArrange = null)
    {
        var (_, services) = await new ApiApplicationFactory<Program>().SetupApplication(
            dbArrange: dbArrange,
            dbName: Guid.NewGuid().ToString(),
            configureSettings: TestSettings.ConfigureAuth());

        return services;
    }
}
