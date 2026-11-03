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
/// Права внутри пространства (спец. authorization, 1.2 и 5): право действует только
/// в своём пространстве, заголовок обязателен и сверяется с параметрами запроса
/// </summary>
public class WorkspacePermissionsTests
{
    [Fact]
    public async Task ReturnsBadRequestWhenWorkspaceHeaderIsMissing()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
        }, token);

        var response = await client.GetAsync($"/api/Invitation/GetList?workspaceId={workspace.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsBadRequestWhenWorkspaceHeaderIsNotGuid()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
        }, token);
        client.DefaultRequestHeaders.Add(WorkspaceConstants.Header, "rogaikopyta");

        var response = await client.GetAsync($"/api/Invitation/GetList?workspaceId={workspace.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Все права владельца — но в своём пространстве. В чужом, названном заголовком,
    /// у него нет ничего
    /// </summary>
    [Fact]
    public async Task OwnerOfOneWorkspaceIsForbiddenInAnother()
    {
        var (workspace, _, oleg) = Given.Owner("Рога и Копыта");
        var (ownWorkspace, token, vasya) = Given.Owner("Пончики и Плюшки", "Vasya");

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, ownWorkspace);
            db.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);

        var response = await client.GetAsync($"/api/Invitation/GetList?workspaceId={workspace.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Заголовок своего пространства, а в query — чужое: право, выданное в своём,
    /// не должно открывать чужое, названное в параметрах
    /// </summary>
    [Fact]
    public async Task ForbidsQueryWorkspaceDifferentFromHeader()
    {
        var (workspace, _, oleg) = Given.Owner("Рога и Копыта");
        var (ownWorkspace, token, vasya) = Given.Owner("Пончики и Плюшки", "Vasya");
        var invitation = Given.Invitation(workspace.Id, oleg.Id);

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, ownWorkspace);
            db.Profiles.AddRange(oleg, vasya);
            db.WorkspaceInvitations.Add(invitation);
        }, token, ownWorkspace.Id);

        var response = await client.GetAsync($"/api/Invitation/GetList?workspaceId={workspace.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// То же для тела: <c>WorkspaceId</c> в DTO не совпал с заголовком — 403,
    /// и права в чужом пространстве не тронуты
    /// </summary>
    [Fact]
    public async Task ForbidsBodyWorkspaceDifferentFromHeader()
    {
        var (workspace, _, oleg) = Given.Owner("Рога и Копыта");
        var (ownWorkspace, token, vasya) = Given.Owner("Пончики и Плюшки", "Vasya");

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.AddRange(workspace, ownWorkspace);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token, ownWorkspace.Id);

        var response = await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = oleg.Id,
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.WorkspaceMemberRead]
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await db.UserInPermissions.AsNoTracking()
            .AnyAsync(x => x.UserId == oleg.Id && x.PermissionId == PermissionEnum.PermissionManagement));
    }

    /// <summary>
    /// Одного <c>WorkspaceMemberRead</c> хватает, чтобы увидеть список выданных ссылок:
    /// <c>Crud</c> подразумевает чтение, и GET пускает по любому из пары
    /// </summary>
    [Fact]
    public async Task InvitationListIsOpenToMemberReadPermission()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (token, dmitry) = new UserBuilder("Dmitry")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();
        var invitation = Given.Invitation(workspace.Id, oleg.Id);

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, dmitry);
            db.WorkspaceInvitations.Add(invitation);
        }, token, workspace.Id);

        var response = await client.GetAsync($"/api/Invitation/GetList?workspaceId={workspace.Id}");

        await response.ShouldBeSuccessful();
        var list = await response.Content.ReadFromJsonAsync<List<WorkspaceInvitationDto>>();
        Assert.Equal(invitation.Token, Assert.Single(list!).Token);
    }

    [Fact]
    public async Task MemberReadCannotCreateInvitation()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (token, dmitry) = new UserBuilder("Dmitry")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, dmitry);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Invitation/Create", new InvitationCreateDto
        {
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.WorkspaceMemberRead]
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Права другого пространства того же человека в ответ не попадают
    /// </summary>
    [Fact]
    public async Task GetPermissionsReturnsPermissionsOfRequestedWorkspace()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var other = new WorkspaceBuilder("Бублики и Сушки").Please();
        var (_, dmitry) = new UserBuilder("Dmitry")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .WithPermissions(other.Id, PermissionEnum.WorkspaceInfoCrud)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, other);
            db.Profiles.AddRange(oleg, dmitry);
        }, token, workspace.Id);

        var response = await client.GetAsync(
            $"/api/Users/GetPermissions?userId={dmitry.Id}&workspaceId={workspace.Id}");

        await response.ShouldBeSuccessful();
        var permissions = await response.Content.ReadFromJsonAsync<List<PermissionEnum>>();
        Assert.Equal([PermissionEnum.WorkspaceMemberRead], permissions);
    }

    /// <summary>
    /// Набор прав — приватная информация: без <c>PermissionManagement</c> не читаются
    /// даже свои
    /// </summary>
    [Fact]
    public async Task GetPermissionsForbiddenWithoutPermissionManagement()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (token, dmitry) = new UserBuilder("Dmitry")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, dmitry);
        }, token, workspace.Id);

        var response = await client.GetAsync(
            $"/api/Users/GetPermissions?userId={dmitry.Id}&workspaceId={workspace.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CanSetPermissions()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, dmitry) = new UserBuilder("Dmitry")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, dmitry);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = dmitry.Id,
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.WorkspaceMemberCrud, PermissionEnum.WorkspaceInfoCrud]
        });

        await response.ShouldBeSuccessful();
        var granted = await db.UserInPermissions.AsNoTracking()
            .Where(x => x.UserId == dmitry.Id && x.WorkspaceId == workspace.Id)
            .Select(x => x.PermissionId)
            .ToListAsync();
        Assert.Equal([PermissionEnum.WorkspaceInfoCrud, PermissionEnum.WorkspaceMemberCrud], granted.Order());
    }

    /// <summary>
    /// Пустой набор прав — 422 у поля, а не 400: отказ виден
    /// из самого запроса. Участник на месте — отказ случился до удаления прежних прав
    /// </summary>
    [Fact]
    public async Task SetPermissionsRejectsEmptySetAtField()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, dmitry) = new UserBuilder("Dmitry")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, dmitry);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = dmitry.Id,
            WorkspaceId = workspace.Id,
            Permissions = []
        });

        await response.ShouldBeValidationWith("Permissions", WorkspaceMessages.EmptyPermissions.Code);
        Assert.True(await db.UserInPermissions.AsNoTracking()
            .AnyAsync(x => x.UserId == dmitry.Id && x.WorkspaceId == workspace.Id));
    }

    /// <summary>
    /// Выдать право, которого нет у самого выдающего, нельзя — иначе ограничение
    /// приглашений обходилось бы здесь в два клика. Нужна база — поэтому 400
    /// </summary>
    [Fact]
    public async Task SetPermissionsRejectsPermissionsBeyondOwn()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (token, boris) = new UserBuilder("Boris")
            .WithPermissions(workspace.Id, PermissionEnum.PermissionManagement)
            .Please();
        var (_, dmitry) = new UserBuilder("Dmitry")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, boris, dmitry);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = dmitry.Id,
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.WorkspaceMemberRead, PermissionEnum.WorkspaceInfoCrud]
        });

        await response.ShouldBeBadRequestWith(WorkspaceMessages.PermissionsExceedOwn);
    }

    /// <summary>
    /// Распорядитель прав не снимает <c>PermissionManagement</c> сам с себя, пока он такой один
    /// </summary>
    [Fact]
    public async Task SetPermissionsKeepsLastPermissionManager()
    {
        var (workspace, token, oleg) = Given.Owner();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = oleg.Id,
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.WorkspaceMemberRead]
        });

        await response.ShouldBeBadRequestWith(WorkspaceMessages.LastPermissionManager);
    }

    /// <summary>
    /// Правка прав не заводит участников: в состав попадают только приглашением
    /// </summary>
    [Fact]
    public async Task SetPermissionsRejectsNonMember()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, stranger) = new UserBuilder("Nikolay").Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, stranger);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = stranger.Id,
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.WorkspaceMemberRead]
        });

        await response.ShouldBeBadRequestWith(WorkspaceMessages.MemberNotFound);
    }
}
