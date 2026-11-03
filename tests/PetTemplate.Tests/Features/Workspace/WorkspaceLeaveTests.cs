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
/// Выход из пространства и удаление участника (спец. authorization, 8)
/// </summary>
public class WorkspaceLeaveTests
{
    /// <summary>
    /// В ответе — новая сессия без покинутого пространства: иначе клиент считал бы его своим
    /// </summary>
    [Fact]
    public async Task LeaveReturnsSessionWithoutLeftWorkspace()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (token, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .WithPrimaryWorkspace(workspace.Id)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);

        var response = await client.PostAsync($"/api/Workspace/Leave?workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Empty(session!.PermissionsByWorkspace);
        // Других пространств нет — указатель основного обнулён тем же сохранением
        Assert.Null(session.Profile.PrimaryWorkspaceId);
        var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == vasya.Id);
        Assert.Null(profile.PrimaryWorkspaceId);
    }

    [Fact]
    public async Task LeavingPrimaryRepointsToAnotherWorkspace()
    {
        var (workspace, _, oleg) = Given.Owner("Рога и Копыта");
        var other = new WorkspaceBuilder("Пончики и Плюшки").Please();
        var (token, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .WithAllPermissions(other.Id)
            .WithPrimaryWorkspace(workspace.Id)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.AddRange(workspace, other);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/Leave?workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == vasya.Id);
        Assert.Equal(other.Id, profile.PrimaryWorkspaceId);
    }

    [Fact]
    public async Task CannotLeaveForeignWorkspace()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var (strangerWorkspace, _, sergey) = Given.Owner("Ёжики в Тумане", "Sergey");

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, strangerWorkspace);
            db.Profiles.AddRange(oleg, sergey);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/Leave?workspaceId={strangerWorkspace.Id}", null);

        await response.ShouldBeBadRequestWith(WorkspaceMessages.NotAMember);
    }

    /// <summary>
    /// Ушёл владелец, остался один участник — ему достаются все права: он теперь
    /// и есть владелец, а половинчатый упёрся бы в первую же закрытую страницу
    /// </summary>
    [Fact]
    public async Task LastRemainingMemberGetsAllPermissions()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/Leave?workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        var granted = await db.UserInPermissions.AsNoTracking()
            .Where(x => x.UserId == vasya.Id && x.WorkspaceId == workspace.Id)
            .Select(x => x.PermissionId)
            .ToListAsync();
        Assert.Equal(Enum.GetValues<PermissionEnum>().Where(x => x != PermissionEnum.None), granted.Order());
    }

    /// <summary>
    /// Оставшихся несколько — каждому только <c>PermissionManagement</c>: выбирать
    /// из них одного не по чему
    /// </summary>
    [Fact]
    public async Task SeveralRemainingMembersGetPermissionManagement()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();
        var (_, petya) = new UserBuilder("Petya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceInfoCrud)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya, petya);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/Leave?workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        var managers = await db.UserInPermissions.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id && x.PermissionId == PermissionEnum.PermissionManagement)
            .Select(x => x.UserId)
            .ToListAsync();
        Assert.Equal(new[] { vasya.Id, petya.Id }.Order(), managers.Order());
        Assert.False(await db.UserInPermissions.AsNoTracking()
            .AnyAsync(x => x.UserId == vasya.Id && x.PermissionId == PermissionEnum.WorkspaceInfoCrud));
    }

    /// <summary>
    /// Вышел последний — пространство удаляется сразу, вместе с приглашениями
    /// </summary>
    [Fact]
    public async Task LastMemberLeavingDeletesWorkspace()
    {
        var (workspace, token, oleg) = Given.Owner();
        var invitation = Given.Invitation(workspace.Id, oleg.Id);

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
            arrange.WorkspaceInvitations.Add(invitation);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/Leave?workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        Assert.False(await db.Workspaces.AsNoTracking().AnyAsync(x => x.Id == workspace.Id));
        Assert.False(await db.WorkspaceInvitations.AsNoTracking().AnyAsync(x => x.WorkspaceId == workspace.Id));
    }

    [Fact]
    public async Task CanRemoveMember()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .WithPrimaryWorkspace(workspace.Id)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);

        var response = await client.PostAsync(
            $"/api/Workspace/RemoveMember?userId={vasya.Id}&workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        Assert.False(await db.UserInPermissions.AsNoTracking().AnyAsync(x => x.UserId == vasya.Id));
        // Указатель основного убранному чинит то же сохранение (спец. authorization, 7.1)
        var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == vasya.Id);
        Assert.Null(profile.PrimaryWorkspaceId);
    }

    [Fact]
    public async Task CannotRemoveSelf()
    {
        var (workspace, token, oleg) = Given.Owner();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
        }, token, workspace.Id);

        var response = await client.PostAsync(
            $"/api/Workspace/RemoveMember?userId={oleg.Id}&workspaceId={workspace.Id}", null);

        await response.ShouldBeBadRequestWith(WorkspaceMessages.CannotRemoveSelf);
    }

    [Fact]
    public async Task CannotRemoveMemberWithoutPermissionManagement()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (token, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberCrud)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);

        var response = await client.PostAsync(
            $"/api/Workspace/RemoveMember?userId={oleg.Id}&workspaceId={workspace.Id}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Убрать участника чужого пространства нельзя: заголовок — своё пространство,
    /// а в query — чужое
    /// </summary>
    [Fact]
    public async Task CannotRemoveMemberOfForeignWorkspace()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var (strangerWorkspace, _, sergey) = Given.Owner("Ёжики в Тумане", "Sergey");
        var (_, vasya) = new UserBuilder("Vasya")
            .WithPermissions(strangerWorkspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.AddRange(workspace, strangerWorkspace);
            arrange.Profiles.AddRange(oleg, sergey, vasya);
        }, token, workspace.Id);

        var response = await client.PostAsync(
            $"/api/Workspace/RemoveMember?userId={vasya.Id}&workspaceId={strangerWorkspace.Id}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await db.UserInPermissions.AsNoTracking().AnyAsync(x => x.UserId == vasya.Id));
    }

    /// <summary>
    /// Ушедший уносит свою память ассистента в этом пространстве, а общая остаётся
    /// оставшимся (спец. assistant, 12.5)
    /// </summary>
    [Fact]
    public async Task LeaverTakesPersonalFactsAway()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, vasya) = new UserBuilder("Vasya").WithAllPermissions(workspace.Id).Please();
        var personal = new AssistantFactBuilder(workspace.Id, "встречи", "встречи обычно на час").Of(oleg.Id).Please();
        var shared = new AssistantFactBuilder(workspace.Id, "илья", "«Илья» — это Илья Петров").Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
            arrange.AssistantFacts.AddRange(personal, shared);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/Leave?workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        var left = await db.AssistantFacts.AsNoTracking().Select(x => x.Id).ToListAsync();
        Assert.Equal([shared.Id], left);
    }

    /// <summary>Убранного участника — так же: его личные умолчания уходят вместе с ним</summary>
    [Fact]
    public async Task RemovedMemberLosesPersonalFacts()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, vasya) = new UserBuilder("Vasya").WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead).Please();
        var vasyas = new AssistantFactBuilder(workspace.Id, "встречи", "встречи на два часа").Of(vasya.Id).Please();
        var olegs = new AssistantFactBuilder(workspace.Id, "встречи", "встречи обычно на час").Of(oleg.Id).Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
            arrange.AssistantFacts.AddRange(vasyas, olegs);
        }, token, workspace.Id);

        var response = await client.PostAsync(
            $"/api/Workspace/RemoveMember?userId={vasya.Id}&workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        var left = await db.AssistantFacts.AsNoTracking().Select(x => x.Id).ToListAsync();
        Assert.Equal([olegs.Id], left);
    }

    /// <summary>Снос пространства уносит его память целиком — и общую, и личную (12.5)</summary>
    [Fact]
    public async Task DeletedWorkspaceTakesMemoryAway()
    {
        var (workspace, token, oleg) = Given.Owner();
        var shared = new AssistantFactBuilder(workspace.Id, "илья", "«Илья» — это Илья Петров").Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
            arrange.AssistantFacts.Add(shared);
        }, token);

        var response = await client.PostAsync($"/api/Workspace/Leave?workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        Assert.False(await db.Workspaces.AsNoTracking().AnyAsync(x => x.Id == workspace.Id));
        Assert.False(await db.AssistantFacts.AsNoTracking().AnyAsync());
    }
}
