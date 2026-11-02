using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Приглашения и роли админов на базе (спец. admin, 3.4): чистка идёт одним
/// <c>ExecuteDelete</c>, роли уходят с пользователем каскадом внешнего ключа, ссылка
/// ищется по хешу в <c>bytea</c>, а гонки принятия держат токен конкуренции и ключ ролей
/// </summary>
public class AdminInvitationsDbTests
{
    private readonly PetTemplate.Db.Entity.Profile _oleg = new UserBuilder("Oleg").Please().profile;

    /// <summary>
    /// Через 21 день после выдачи строка уходит в любом статусе; просроченная,
    /// но моложе — остаётся в списке выданных
    /// </summary>
    [Fact]
    public async Task CleanupRemovesOnlyInvitationsOlderThanRetention()
    {
        // Arrange
        var fresh = Invitation(createdDaysAgo: 0);
        var expiredButYoung = Invitation(createdDaysAgo: 20);
        var oldPending = Invitation(createdDaysAgo: 22);
        var oldAccepted = Invitation(createdDaysAgo: 22, AdminInvitationStatus.Accepted);
        var oldRevoked = Invitation(createdDaysAgo: 30, AdminInvitationStatus.Revoked);

        var services = await Arrange(db =>
        {
            db.Profiles.Add(_oleg);
            db.AdminInvitations.AddRange(fresh, expiredButYoung, oldPending, oldAccepted, oldRevoked);
        });

        using var scope = services.CreateScope();
        var invitations = scope.ServiceProvider.GetRequiredService<AdminInvitationService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        // Act
        var deleted = await invitations.CleanupOld();

        // Assert
        Assert.Equal(3, deleted);
        var left = await db.AdminInvitations.AsNoTracking().Select(x => x.Id).ToListAsync();
        Assert.Equal(2, left.Count);
        Assert.Contains(fresh.Id, left);
        Assert.Contains(expiredButYoung.Id, left);
    }

    /// <summary>Выданная ссылка находится по токену, хотя в базе лежит только его хеш</summary>
    [Fact]
    public async Task IssuedInvitationIsFoundByToken()
    {
        // Arrange
        var services = await Arrange(db =>
        {
            db.Profiles.Add(_oleg);
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, AdminRole.AdminManagement, AdminRole.Accounts));
        });

        using var scope = services.CreateScope();
        var invitations = scope.ServiceProvider.GetRequiredService<AdminInvitationService>();
        var issued = await invitations.Create(new AdminInvitationCreateDto { Roles = [AdminRole.Accounts] }, _oleg.Id);

        // Act
        var preview = await invitations.Preview(issued.Token);
        var unknown = await invitations.Preview(Guid.NewGuid());

        // Assert
        Assert.Equal(AdminInvitationStatus.Pending, preview!.Status);
        Assert.Equal([AdminRole.Accounts], preview.Roles);
        Assert.Null(unknown);
    }

    /// <summary>Удаление пользователя в продукте уносит и его админские роли (3.1)</summary>
    [Fact]
    public async Task DeletingUserRemovesAdminRoles()
    {
        // Arrange
        var vasya = new UserBuilder("Vasya").WithContacts("vasya@example.com", "+79990000002").Please().profile;

        var services = await Arrange(db =>
        {
            db.Profiles.AddRange(_oleg, vasya);
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, AdminRole.AdminManagement));
            db.UserInAdminRoles.AddRange(Given.AdminRoles(vasya.Id, AdminRole.Accounts));
        });

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        // Act
        await db.Users.Where(x => x.Id == vasya.Id).ExecuteDeleteAsync();

        // Assert
        var left = await db.UserInAdminRoles.AsNoTracking().Select(x => x.UserId).ToListAsync();
        Assert.Equal([_oleg.Id], left);
    }

    /// <summary>
    /// Две ссылки с общей ролью, принятые одним человеком одновременно: проигравший
    /// упирается в ключ (UserId, Role) и получает тот же отказ, что второй принимающий
    /// одной ссылки, — <c>null</c> (наружу <c>404</c>), а не исключение (500)
    /// </summary>
    [Fact]
    public async Task ConcurrentAcceptOfInvitationsWithSharedRoleDoesNotThrow()
    {
        // Arrange
        var vasya = new UserBuilder("Vasya").WithContacts("vasya@example.com", "+79990000002").Please().profile;
        var tokens = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var services = await Arrange(db =>
        {
            db.Profiles.AddRange(_oleg, vasya);
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, AdminRole.AdminManagement, AdminRole.Accounts));
            db.AdminInvitations.AddRange(tokens.Select(token =>
            {
                var invitation = Invitation(createdDaysAgo: 0);
                invitation.TokenHash = AdminInvitation.HashOf(token);
                return invitation;
            }));
        });

        // Act: десять пар одновременных принятий — гонка случается не в каждой;
        // исключение из любой пары роняет тест
        var rounds = new List<AdminDto?[]>();
        for (var round = 0; round < 10; round++)
        {
            rounds.Add(await Task.WhenAll(tokens.Select(token => Task.Run(async () =>
            {
                using var scope = services.CreateScope();
                var invitations = scope.ServiceProvider.GetRequiredService<AdminInvitationService>();
                return await invitations.Accept(token, new() { Login = "Vasya", Password = UserBuilder.SuperAdminPassword });
            }))));

            using var reset = services.CreateScope();
            var db = reset.ServiceProvider.GetRequiredService<ApplicationContext>();
            await db.UserInAdminRoles.Where(x => x.UserId == vasya.Id).ExecuteDeleteAsync();
            await db.AdminInvitations.ExecuteUpdateAsync(x => x
                .SetProperty(i => i.Status, AdminInvitationStatus.Pending)
                .SetProperty(i => i.AcceptedByUserId, (Guid?)null));
        }

        // Assert: в каждой паре хотя бы одна ссылка принята
        Assert.All(rounds, pair => Assert.Contains(pair, x => x != null));
    }

    /// <summary>
    /// Одну ссылку разом принимают двое (3.4): одноразовая она и в гонке — роли
    /// получает один, второй упирается в условный <c>UPDATE … WHERE Status = Pending</c>,
    /// и его строки ролей откатываются вместе с ним
    /// </summary>
    [Fact]
    public async Task ConcurrentAcceptOfOneInvitationGrantsRolesOnce()
    {
        // Arrange
        var vasya = new UserBuilder("Vasya").WithContacts("vasya@example.com", "+79990000002").Please().profile;
        var petya = new UserBuilder("Petya").WithContacts("petya@example.com", "+79990000003").Please().profile;
        var token = Guid.NewGuid();
        var services = await Arrange(db =>
        {
            db.Profiles.AddRange(_oleg, vasya, petya);
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, AdminRole.AdminManagement, AdminRole.Accounts));
            var invitation = Invitation(createdDaysAgo: 0);
            invitation.TokenHash = AdminInvitation.HashOf(token);
            db.AdminInvitations.Add(invitation);
        });

        // Act: десять раундов — гонка случается не в каждом, а правило обязано держаться в любом
        var granted = new List<Guid[]>();
        for (var round = 0; round < 10; round++)
        {
            await Task.WhenAll(new[] { "Vasya", "Petya" }.Select(login => Task.Run(async () =>
            {
                using var scope = services.CreateScope();
                var invitations = scope.ServiceProvider.GetRequiredService<AdminInvitationService>();
                return await invitations.Accept(token, new() { Login = login, Password = UserBuilder.SuperAdminPassword });
            })));

            using var reset = services.CreateScope();
            var db = reset.ServiceProvider.GetRequiredService<ApplicationContext>();
            granted.Add(await db.UserInAdminRoles
                .Where(x => x.UserId != _oleg.Id)
                .Select(x => x.UserId)
                .ToArrayAsync());
            await db.UserInAdminRoles.Where(x => x.UserId != _oleg.Id).ExecuteDeleteAsync();
            await db.AdminInvitations.ExecuteUpdateAsync(x => x
                .SetProperty(i => i.Status, AdminInvitationStatus.Pending)
                .SetProperty(i => i.AcceptedByUserId, (Guid?)null));
        }

        // Assert: в каждом раунде роль ровно у одного из двоих
        Assert.All(granted, x => Assert.Single(x));
    }

    private AdminInvitation Invitation(int createdDaysAgo, AdminInvitationStatus status = AdminInvitationStatus.Pending)
    {
        var created = DateTime.UtcNow.AddDays(-createdDaysAgo).AddMinutes(-1);

        return new AdminInvitation
        {
            Id = Guid.CreateVersion7(),
            TokenHash = AdminInvitation.HashOf(Guid.NewGuid()),
            Roles = AdminRole.Accounts,
            Status = status,
            CreatedUtc = created,
            ExpiresUtc = created.AddDays(3),
            CreatedByUserId = _oleg.Id
        };
    }

    private static Task<IServiceProvider> Arrange(Action<ApplicationContext> dbArrange) =>
        Given.Services(dbArrange);
}
