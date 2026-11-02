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
/// Общий мир тестов «Аккаунтов» (спец. accounts, 8.5): админ Борис с ролью
/// <see cref="AdminRole.Accounts"/>, Маша — владелец «Пончиков и Плюшек» с привязанным Telegram,
/// Одинокий — без пространств. Клиент входит в админку Борисом — тем же путём, что человек:
/// <c>Admin/Login</c> и токен своей схемы
/// </summary>
public class AdminAccountsWorld
{
    public const long MashaTelegram = 777001;

    public readonly PetTemplate.Db.Entity.Profile Admin;
    public readonly PetTemplate.Db.Entity.Workspace Ponchiki = new WorkspaceBuilder("Пончики и Плюшки").Please();
    public readonly PetTemplate.Db.Entity.Profile Masha;
    public readonly PetTemplate.Db.Entity.Profile Lonely = new UserBuilder("Lonely")
        .WithContacts("lonely@ponchiki.ru", "+79990000002")
        .Please().profile;

    public AdminAccountsWorld(params AdminRole[] roles)
    {
        Admin = new UserBuilder("Boris")
            .WithContacts("boris@example.com", "+79990000009")
            .WithAdminRoles(roles.Length == 0 ? [AdminRole.Accounts] : roles)
            .Please().profile;
        Masha = new UserBuilder("Masha", telegramId: MashaTelegram)
            .WithContacts("masha@ponchiki.ru", "+79990000001")
            .WithAllPermissions(Ponchiki.Id)
            .WithPrimaryWorkspace(Ponchiki.Id)
            .Please().profile;
    }

    public void Arrange(ApplicationContext x)
    {
        // Пространство раньше прав: на него ссылается внешний ключ
        x.Workspaces.Add(Ponchiki);
        x.Profiles.AddRange(Admin, Masha, Lonely);
    }

    /// <summary>Клиент, вошедший в админку Борисом; база — своя на тест</summary>
    public async Task<(HttpClient client, ApplicationContext db)> Client(Action<ApplicationContext>? extra = null)
    {
        var (client, db) = await Given.ApiClient(x =>
        {
            Arrange(x);
            extra?.Invoke(x);
        });

        var response = await client.PostAsJsonAsync("/api/Admin/Login",
            new LoginDto { Login = "Boris", Password = UserBuilder.SuperAdminPassword });
        await response.ShouldBeSuccessful();
        client.UseAdminSession((await response.Content.ReadFromJsonAsync<AdminLoginResultDto>())!);

        return (client, db);
    }
}
