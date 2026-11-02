using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Workspace;

/// <summary>
/// Создание, список, правка и основное пространство (спец. authorization, 3 и 7)
/// </summary>
public class WorkspaceTests
{
    /// <summary>
    /// Создатель получает все права, а ответ несёт новую сессию с ними: без перевыпуска
    /// сервер считал бы создателя посторонним в его же пространстве (спец. authorization, 2.2)
    /// </summary>
    [Fact]
    public async Task CreateReturnsSessionWithOwnerPermissions()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(db => db.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Workspace/Create", new WorkspaceDto { Name = "Котики и Собачки" });

        await response.ShouldBeSuccessful();
        var created = await response.Content.ReadFromJsonAsync<WorkspaceCreatedDto>();
        var owner = created!.Authentication.PermissionsByWorkspace[created.WorkspaceId];
        Assert.Equal(Enum.GetValues<PermissionEnum>().Where(x => x != PermissionEnum.None), owner.Order());

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(created.Authentication.Token).Claims;
        Assert.Contains(claims, x => x.Type == CustomClaimTypes.WorkspacePermission
                                     && x.Value == $"{created.WorkspaceId}|{(int)PermissionEnum.PermissionManagement}");
    }

    /// <summary>
    /// Первое пространство становится основным само (спец. authorization, 7.1)
    /// </summary>
    [Fact]
    public async Task FirstWorkspaceBecomesPrimary()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, db) = await Given.ApiClient(arrange => arrange.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Workspace/Create", new WorkspaceDto { Name = "Винтики и Шпунтики" });

        await response.ShouldBeSuccessful();
        var created = await response.Content.ReadFromJsonAsync<WorkspaceCreatedDto>();
        Assert.Equal(created!.WorkspaceId, created.Authentication.Profile.PrimaryWorkspaceId);
        var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.Equal(created.WorkspaceId, profile.PrimaryWorkspaceId);
    }

    [Fact]
    public async Task SecondWorkspaceKeepsPrimary()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
        }, token);

        var response = await client.PostAsJsonAsync("/api/Workspace/Create", new WorkspaceDto { Name = "Крылышки и Ножки" });

        await response.ShouldBeSuccessful();
        var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.Equal(workspace.Id, profile.PrimaryWorkspaceId);
    }

    [Fact]
    public async Task CreateRejectsBlankName()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(db => db.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Workspace/Create", new WorkspaceDto { Name = "   " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>
    /// Регистрация с названием — аккаунт и пространство одним сохранением, токен входа
    /// уже с правами нового пространства (спец. authorization, 3, п. 1)
    /// </summary>
    [Fact]
    public async Task RegisterWithWorkspaceNameCreatesWorkspace()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = "Andrey",
            Password = "password123",
            PasswordConfirm = "password123",
            WorkspaceName = "Бублики и Сушки"
        });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        var workspaceId = Assert.Single(result!.PermissionsByWorkspace.Keys);
        Assert.Contains(PermissionEnum.PermissionManagement, result.PermissionsByWorkspace[workspaceId]);
        Assert.Equal(workspaceId, result.Profile.PrimaryWorkspaceId);
    }

    /// <summary>
    /// Список — только свои пространства, с числом участников
    /// </summary>
    [Fact]
    public async Task GetListReturnsOnlyOwnWorkspacesWithMembersCount()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var (strangerWorkspace, _, sergey) = Given.Owner("Ёжики в Тумане", "Sergey");
        var (_, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, strangerWorkspace);
            db.Profiles.AddRange(oleg, sergey, vasya);
        }, token);

        var response = await client.GetAsync("/api/Workspace/GetList");

        await response.ShouldBeSuccessful();
        var list = await response.Content.ReadFromJsonAsync<List<WorkspaceDto>>();
        var own = Assert.Single(list!);
        Assert.Equal("Рога и Копыта", own.Name);
        Assert.Equal(2, own.MembersCount);
    }

    [Fact]
    public async Task CanRenameWithWorkspaceInfoCrud()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Workspace/Update", new WorkspaceDto { Name = "Шарики и Палочки" });

        await response.ShouldBeSuccessful();
        var saved = await db.Workspaces.AsNoTracking().SingleAsync(x => x.Id == workspace.Id);
        Assert.Equal("Шарики и Палочки", saved.Name);
    }

    [Fact]
    public async Task CannotRenameWithoutWorkspaceInfoCrud()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (token, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberCrud, PermissionEnum.PermissionManagement)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Workspace/Update", new WorkspaceDto { Name = "Захвачено" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// <c>Id</c> из тела не читается: правится пространство из заголовка, на которое
    /// проверено право, а не названное в теле чужое
    /// </summary>
    [Fact]
    public async Task RenameIgnoresForeignIdInBody()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var (strangerWorkspace, _, sergey) = Given.Owner("Ёжики в Тумане", "Sergey");

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.AddRange(workspace, strangerWorkspace);
            arrange.Profiles.AddRange(oleg, sergey);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Workspace/Update",
            new WorkspaceDto { Id = strangerWorkspace.Id, Name = "Захвачено" });

        await response.ShouldBeSuccessful();
        var stranger = await db.Workspaces.AsNoTracking().SingleAsync(x => x.Id == strangerWorkspace.Id);
        Assert.Equal("Ёжики в Тумане", stranger.Name);
    }

    [Fact]
    public async Task CanSetOwnWorkspaceAsPrimary()
    {
        var (workspace, _, oleg) = Given.Owner("Рога и Копыта");
        var second = new WorkspaceBuilder("Пончики и Плюшки").Please();
        var (token, vasya) = new UserBuilder("Vasya")
            .WithAllPermissions(workspace.Id)
            .WithPermissions(second.Id, PermissionEnum.WorkspaceMemberRead)
            .WithPrimaryWorkspace(workspace.Id)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.AddRange(workspace, second);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/SetPrimary?workspaceId={second.Id}", null);

        await response.ShouldBeSuccessful();
        var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == vasya.Id);
        Assert.Equal(second.Id, profile.PrimaryWorkspaceId);
    }

    [Fact]
    public async Task CannotSetForeignWorkspaceAsPrimary()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var (strangerWorkspace, _, sergey) = Given.Owner("Ёжики в Тумане", "Sergey");

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, strangerWorkspace);
            db.Profiles.AddRange(oleg, sergey);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/SetPrimary?workspaceId={strangerWorkspace.Id}", null);

        await response.ShouldBeBadRequestWith(WorkspaceMessages.WorkspaceUnavailable);
    }

    /// <summary>
    /// Указатель в профиле ведёт в пространство, которого у человека уже нет, — вход
    /// отдаёт первое доступное, а не мёртвый указатель (спец. authorization, 7.1)
    /// </summary>
    [Fact]
    public async Task LoginFallsBackToAvailableWorkspace()
    {
        var (workspace, _, oleg) = Given.Owner("Рога и Копыта");
        var own = new WorkspaceBuilder("Пончики и Плюшки").Please();
        var (_, vasya) = new UserBuilder("Vasya")
            .WithPermissions(own.Id, PermissionEnum.WorkspaceMemberRead)
            .WithPrimaryWorkspace(workspace.Id)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, own);
            db.Profiles.AddRange(oleg, vasya);
        });

        // Хеш пароля в билдере — от этого пароля
        var response = await client.PostAsJsonAsync("/api/Auth/LogIn",
            new LoginDto { Login = "Vasya", Password = UserBuilder.SuperAdminTelegramAuth });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal(own.Id, result!.Profile.PrimaryWorkspaceId);
    }
}
