using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Invitation;

/// <summary>
/// Приглашения по ссылке (спец. authorization, 6)
/// </summary>
public class InvitationTests
{
    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CanCreateInvitation()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Invitation/Create", new InvitationCreateDto
        {
            WorkspaceId = workspace.Id,
            InviteeName = "  Валера  ",
            Permissions = [PermissionEnum.WorkspaceMemberRead]
        });

        await response.ShouldBeSuccessful();
        var invitation = await response.Content.ReadFromJsonAsync<WorkspaceInvitationDto>();
        Assert.Equal(InvitationStatusEnum.Active, invitation!.Status);
        Assert.Equal("Валера", invitation.InviteeName);
        // Токен — v4: у v7 метка времени, и выданные подряд лежали бы предсказуемо рядом
        Assert.Equal(4, invitation.Token.Version);
    }

    /// <summary>
    /// Приглашение без прав — 422 у поля: по такой ссылке нельзя даже войти
    /// </summary>
    [Fact]
    public async Task CreateRejectsEmptyPermissionsAtField()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Invitation/Create", new InvitationCreateDto
        {
            WorkspaceId = workspace.Id,
            Permissions = []
        });

        await response.ShouldBeValidationWith("Permissions", InvitationMessages.EmptyPermissions.Code);
    }

    /// <summary>
    /// <c>WorkspaceMemberCrud</c> не превращается в способ выписать себе
    /// <c>PermissionManagement</c> через самоприглашение
    /// </summary>
    [Fact]
    public async Task CreateRejectsPermissionsBeyondOwn()
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

        var response = await client.PostAsJsonAsync("/api/Invitation/Create", new InvitationCreateDto
        {
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.PermissionManagement]
        });

        await response.ShouldBeBadRequestWith(WorkspaceMessages.PermissionsExceedOwn);
    }

    // ── Preview ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewShowsWorkspaceAndInviter()
    {
        var (workspace, _, oleg) = Given.Owner("Пончики и Плюшки");
        var invitation = new WorkspaceInvitationBuilder(workspace.Id, oleg.Id)
            .WithPermissions(PermissionEnum.WorkspaceMemberRead)
            .WithInviteeName("Валера")
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
            db.WorkspaceInvitations.Add(invitation);
        });

        var response = await client.GetAsync($"/api/Invitation/Preview?token={invitation.Token}");

        await response.ShouldBeSuccessful();
        var preview = await response.Content.ReadFromJsonAsync<InvitationPreviewDto>();
        Assert.Equal(InvitationStatusEnum.Active, preview!.Status);
        Assert.Equal("Пончики и Плюшки", preview.WorkspaceName);
        Assert.Equal("Oleg", preview.InviterName);
        Assert.Equal("Валера", preview.InviteeName);
    }

    /// <summary>
    /// Ненайденный токен отвечает так же, как просроченный, и о пространстве не рассказывает
    /// </summary>
    [Fact]
    public async Task PreviewOfUnknownTokenLooksExpired()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.GetAsync($"/api/Invitation/Preview?token={Guid.NewGuid()}");

        await response.ShouldBeSuccessful();
        var preview = await response.Content.ReadFromJsonAsync<InvitationPreviewDto>();
        Assert.Equal(InvitationStatusEnum.Expired, preview!.Status);
        Assert.Null(preview.WorkspaceName);
    }

    [Fact]
    public async Task PreviewOfRevokedHidesWorkspace()
    {
        var (workspace, _, oleg) = Given.Owner();
        var invitation = new WorkspaceInvitationBuilder(workspace.Id, oleg.Id)
            .WithPermissions(PermissionEnum.WorkspaceMemberRead)
            .Revoked()
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
            db.WorkspaceInvitations.Add(invitation);
        });

        var response = await client.GetAsync($"/api/Invitation/Preview?token={invitation.Token}");

        var preview = await response.Content.ReadFromJsonAsync<InvitationPreviewDto>();
        Assert.Equal(InvitationStatusEnum.Revoked, preview!.Status);
        Assert.Null(preview.WorkspaceName);
    }

    // ── Accept ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Принявший получает права приглашения, ссылка расходуется, а в ответе — сессия
    /// уже с новым пространством; единственное пространство становится основным
    /// </summary>
    [Fact]
    public async Task AcceptGrantsPermissionsAndReturnsSession()
    {
        var (workspace, _, oleg) = Given.Owner();
        var invitation = Given.Invitation(workspace.Id, oleg.Id,
            PermissionEnum.WorkspaceMemberRead, PermissionEnum.WorkspaceInfoCrud);
        var (token, vasya) = new UserBuilder("Vasya").Please();

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
            arrange.WorkspaceInvitations.Add(invitation);
        }, token);

        var response = await client.PostAsJsonAsync("/api/Invitation/Accept", new InvitationAcceptDto { Token = invitation.Token });

        await response.ShouldBeSuccessful();
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal(
            [PermissionEnum.WorkspaceInfoCrud, PermissionEnum.WorkspaceMemberRead],
            session!.PermissionsByWorkspace[workspace.Id].Order());
        Assert.Equal(workspace.Id, session.Profile.PrimaryWorkspaceId);

        var consumed = await db.WorkspaceInvitations.AsNoTracking().SingleAsync(x => x.Id == invitation.Id);
        Assert.Equal(vasya.Id, consumed.AcceptedBy);
    }

    /// <summary>
    /// Уже участник — отказ, и ссылка не расходуется: приглашали, возможно, не его
    /// </summary>
    [Fact]
    public async Task AcceptByMemberDoesNotConsumeInvitation()
    {
        var (workspace, token, oleg) = Given.Owner();
        var invitation = Given.Invitation(workspace.Id, oleg.Id);

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
            arrange.WorkspaceInvitations.Add(invitation);
        }, token);

        var response = await client.PostAsJsonAsync("/api/Invitation/Accept", new InvitationAcceptDto { Token = invitation.Token });

        await response.ShouldBeBadRequestWith(InvitationMessages.AlreadyMember);
        var untouched = await db.WorkspaceInvitations.AsNoTracking().SingleAsync(x => x.Id == invitation.Id);
        Assert.Null(untouched.AcceptedBy);
    }

    [Fact]
    public async Task CannotAcceptTwice()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (_, vasya) = new UserBuilder("Vasya").WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead).Please();
        var invitation = new WorkspaceInvitationBuilder(workspace.Id, oleg.Id)
            .WithPermissions(PermissionEnum.WorkspaceMemberRead)
            .Accepted(vasya.Id)
            .Please();
        var (token, petya) = new UserBuilder("Petya").Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, vasya, petya);
            db.WorkspaceInvitations.Add(invitation);
        }, token);

        var response = await client.PostAsJsonAsync("/api/Invitation/Accept", new InvitationAcceptDto { Token = invitation.Token });

        await response.ShouldBeBadRequestWith(InvitationMessages.Accepted);
    }

    // ── Register ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterByInvitationCreatesMember()
    {
        var (workspace, _, oleg) = Given.Owner();
        var invitation = Given.Invitation(workspace.Id, oleg.Id, PermissionEnum.WorkspaceMemberRead);

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
            db.WorkspaceInvitations.Add(invitation);
        });

        var response = await client.PostAsJsonAsync("/api/Invitation/Register", new InvitationRegisterDto
        {
            Token = invitation.Token,
            UserName = "Maxim",
            Password = "password123",
            PasswordConfirm = "password123"
        });

        await response.ShouldBeSuccessful();
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal("Maxim", session!.Profile.UserName);
        Assert.Equal([PermissionEnum.WorkspaceMemberRead], session.PermissionsByWorkspace[workspace.Id]);
        Assert.Equal(workspace.Id, session.Profile.PrimaryWorkspaceId);
    }

    /// <summary>
    /// Занятый логин — отказ, и ссылка не израсходована: всё уходит одним сохранением
    /// </summary>
    [Fact]
    public async Task RegisterWithTakenUserNameKeepsInvitation()
    {
        var (workspace, _, oleg) = Given.Owner();
        var invitation = Given.Invitation(workspace.Id, oleg.Id);

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
            arrange.WorkspaceInvitations.Add(invitation);
        });

        var response = await client.PostAsJsonAsync("/api/Invitation/Register", new InvitationRegisterDto
        {
            Token = invitation.Token,
            UserName = "oleg",
            Password = "password123",
            PasswordConfirm = "password123"
        });

        await response.ShouldBeBadRequestWith(InvitationMessages.UserNameTaken);
        var untouched = await db.WorkspaceInvitations.AsNoTracking().SingleAsync(x => x.Id == invitation.Id);
        Assert.Null(untouched.AcceptedBy);
    }

    // ── Revoke ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CanRevokeInvitation()
    {
        var (workspace, token, oleg) = Given.Owner();
        var invitation = Given.Invitation(workspace.Id, oleg.Id);

        var (client, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
            arrange.WorkspaceInvitations.Add(invitation);
        }, token, workspace.Id);

        var response = await client.PostAsync(
            $"/api/Invitation/Revoke?id={invitation.Id}&workspaceId={workspace.Id}", null);

        await response.ShouldBeSuccessful();
        var revoked = await db.WorkspaceInvitations.AsNoTracking().SingleAsync(x => x.Id == invitation.Id);
        Assert.NotNull(revoked.RevokedAtUtc);
    }

    [Fact]
    public async Task CannotRevokeAcceptedInvitation()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, vasya) = new UserBuilder("Vasya").WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead).Please();
        var invitation = new WorkspaceInvitationBuilder(workspace.Id, oleg.Id)
            .WithPermissions(PermissionEnum.WorkspaceMemberRead)
            .Accepted(vasya.Id)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, vasya);
            db.WorkspaceInvitations.Add(invitation);
        }, token, workspace.Id);

        var response = await client.PostAsync(
            $"/api/Invitation/Revoke?id={invitation.Id}&workspaceId={workspace.Id}", null);

        await response.ShouldBeBadRequestWith(InvitationMessages.CannotRevokeAccepted);
    }

    /// <summary>
    /// Приглашение чужого пространства своим правом не отозвать: ищем его только
    /// в пространстве из заголовка — 404
    /// </summary>
    [Fact]
    public async Task CannotRevokeForeignInvitation()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var (strangerWorkspace, _, sergey) = Given.Owner("Ёжики в Тумане", "Sergey");
        var foreign = Given.Invitation(strangerWorkspace.Id, sergey.Id);

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, strangerWorkspace);
            db.Profiles.AddRange(oleg, sergey);
            db.WorkspaceInvitations.Add(foreign);
        }, token, workspace.Id);

        var response = await client.PostAsync(
            $"/api/Invitation/Revoke?id={foreign.Id}&workspaceId={workspace.Id}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
