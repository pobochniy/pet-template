using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Workspace;

/// <summary>
/// Суточная чистка (спец. authorization, 6.2 и 8). Воркер в тестах снят
/// (<see cref="ApiApplicationFactory{TStartup}"/>), поэтому тест зовёт сервис сам
/// </summary>
public class WorkspaceCleanupTests
{
    [Fact]
    public async Task DeletesAbandonedWorkspaceAfterRetention()
    {
        var abandoned = new WorkspaceBuilder("Ёжики в Тумане")
            .CreatedAt(DateTime.UtcNow - WorkspaceConstants.AbandonedRetention - TimeSpan.FromDays(1))
            .Please();
        var (_, oleg) = new UserBuilder("Oleg").WithPrimaryWorkspace(abandoned.Id).Please();

        var (_, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(abandoned);
            arrange.Profiles.Add(oleg);
        });

        var deleted = await Cleanup(db);

        Assert.Equal(1, deleted);
        Assert.False(await db.Workspaces.AsNoTracking().AnyAsync(x => x.Id == abandoned.Id));
        // Указатель на удалённое не остаётся ни у кого
        var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.Null(profile.PrimaryWorkspaceId);
    }

    /// <summary>
    /// Пустое, но свежее — живёт: удалять назавтра после «создал и не успел позвать»
    /// было бы сюрпризом
    /// </summary>
    [Fact]
    public async Task KeepsRecentEmptyWorkspace()
    {
        var fresh = new WorkspaceBuilder("Котики и Собачки").Please();

        var (_, db) = await Given.ApiClient(arrange => arrange.Workspaces.Add(fresh));

        var deleted = await Cleanup(db);

        Assert.Equal(0, deleted);
        Assert.True(await db.Workspaces.AsNoTracking().AnyAsync(x => x.Id == fresh.Id));
    }

    /// <summary>
    /// Старое, но с участником — не заброшенное: нужны оба критерия
    /// </summary>
    [Fact]
    public async Task KeepsOldWorkspaceWithMembers()
    {
        var old = new WorkspaceBuilder("Винтики и Шпунтики")
            .CreatedAt(DateTime.UtcNow - WorkspaceConstants.AbandonedRetention - TimeSpan.FromDays(30))
            .Please();
        var (_, oleg) = new UserBuilder("Oleg").WithPermissions(old.Id, PermissionEnum.WorkspaceMemberRead).Please();

        var (_, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(old);
            arrange.Profiles.Add(oleg);
        });

        var deleted = await Cleanup(db);

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task DeletesInvitationsAfterRetention()
    {
        var (workspace, _, oleg) = Given.Owner();
        var old = new WorkspaceInvitationBuilder(workspace.Id, oleg.Id)
            .WithPermissions(PermissionEnum.WorkspaceMemberRead)
            .CreatedAt(DateTime.UtcNow - InvitationConstants.Retention - TimeSpan.FromDays(1))
            .Please();
        var fresh = Given.Invitation(workspace.Id, oleg.Id);

        var (_, db) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
            arrange.WorkspaceInvitations.AddRange(old, fresh);
        });

        var workspaces = new WorkspaceService(db);
        var emails = new EmailConfirmation(db, new AccountTokens(db), Options.Create(new AccountOptions()));
        var deleted = await new WorkspaceInvitationService(db, new AuthService(db, workspaces, emails, new UserNameGenerator(db))).CleanupOld();

        Assert.Equal(1, deleted);
        var left = await db.WorkspaceInvitations.AsNoTracking().Select(x => x.Id).ToListAsync();
        Assert.Equal([fresh.Id], left);
    }

    private static Task<int> Cleanup(PetTemplate.Db.Entity.ApplicationContext db) =>
        new WorkspaceService(db).CleanupAbandoned();
}
