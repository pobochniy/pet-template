using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Админы и их роли (спец. admin, 3.1 и 3.4): выдача не больше своих, последняя
/// роль снимается только удалением, последний <c>AdminManagement</c> не снимается вовсе
/// </summary>
public class AdminAdminsApiTests
{
    private readonly PetTemplate.Db.Entity.Profile _oleg = new UserBuilder("Oleg").Please().profile;

    private readonly PetTemplate.Db.Entity.Profile _vasya = new UserBuilder("Vasya").WithContacts("vasya@example.com", "+79990000021").Please().profile;

    [Fact]
    public async Task CanSeeAdminsWithRoles()
    {
        // Arrange
        var (client, _) = await Client(
            [AdminRole.AdminManagement, AdminRole.Accounts],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.Accounts)));

        // Act
        var response = await client.GetAsync("/api/Admin/Admins");

        // Assert
        await response.ShouldBeSuccessful();
        var admins = await response.Content.ReadFromJsonAsync<AdminMemberDto[]>();
        Assert.Equal(["Oleg", "Vasya"], admins!.Select(x => x.UserName));
        Assert.Equal([AdminRole.AdminManagement, AdminRole.Accounts], admins[0].Roles.Select(x => x.Role));
        Assert.Equal([AdminRole.Accounts], admins[1].Roles.Select(x => x.Role));
    }

    [Fact]
    public async Task CanGrantOwnRole()
    {
        // Arrange
        var (client, db) = await Client(
            [AdminRole.AdminManagement, AdminRole.Accounts],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.Accounts)));

        // Act
        var response = await client.PostAsync($"/api/Admin/Admins/{_vasya.Id}/Roles/{(int)AdminRole.AdminManagement}", null);

        // Assert
        await response.ShouldBeSuccessful();
        var granted = await db.UserInAdminRoles.AsNoTracking()
            .SingleAsync(x => x.UserId == _vasya.Id && x.Role == AdminRole.AdminManagement);
        Assert.Equal(_oleg.Id, granted.GrantedByUserId);
    }

    /// <summary>Иначе <c>AdminManagement</c> выписывал бы кому угодно <c>Accounts</c>, которой нет у него самого</summary>
    [Fact]
    public async Task CannotGrantRoleNotOwned()
    {
        // Arrange
        var (client, db) = await Client(
            [AdminRole.AdminManagement],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.AdminManagement)));

        // Act
        var response = await client.PostAsync($"/api/Admin/Admins/{_vasya.Id}/Roles/{(int)AdminRole.Accounts}", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.Role == AdminRole.Accounts));
    }

    /// <summary>Роль в токене пережила снятие — раздавать её дальше она не должна</summary>
    [Fact]
    public async Task CannotGrantRoleRevokedAfterLogin()
    {
        // Arrange
        var (client, db) = await Client(
            [AdminRole.AdminManagement],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.AdminManagement)),
            tokenRoles: [AdminRole.AdminManagement, AdminRole.Accounts]);

        // Act
        var response = await client.PostAsync($"/api/Admin/Admins/{_vasya.Id}/Roles/{(int)AdminRole.Accounts}", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.Role == AdminRole.Accounts));
    }

    /// <summary>Админы заводятся приглашением, а не выдачей роли по идентификатору</summary>
    [Fact]
    public async Task CannotGrantRoleToNonAdmin()
    {
        // Arrange
        var (client, db) = await Client([AdminRole.AdminManagement, AdminRole.Accounts]);

        // Act
        var response = await client.PostAsync($"/api/Admin/Admins/{_vasya.Id}/Roles/{(int)AdminRole.Accounts}", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _vasya.Id));
    }

    [Fact]
    public async Task CanRevokeRole()
    {
        // Arrange
        var (client, db) = await Client(
            [AdminRole.AdminManagement],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.AdminManagement, AdminRole.Accounts)));

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Admins/{_vasya.Id}/Roles/{(int)AdminRole.Accounts}");

        // Assert
        await response.ShouldBeSuccessful();
        var left = await db.UserInAdminRoles.AsNoTracking().Where(x => x.UserId == _vasya.Id).Select(x => x.Role).ToListAsync();
        Assert.Equal([AdminRole.AdminManagement], left);
    }

    /// <summary>Снятие последней роли — это удаление админа, и делается оно явно (3.1)</summary>
    [Fact]
    public async Task CannotRevokeLastRole()
    {
        // Arrange
        var (client, db) = await Client(
            [AdminRole.AdminManagement],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.Accounts)));

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Admins/{_vasya.Id}/Roles/{(int)AdminRole.Accounts}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _vasya.Id));
    }

    /// <summary>Вернуть <c>AdminManagement</c> в систему было бы некому, кроме строки в базе</summary>
    [Fact]
    public async Task CannotRevokeLastAdminManagement()
    {
        // Arrange
        var (client, db) = await Client([AdminRole.AdminManagement, AdminRole.Accounts]);

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Admins/{_oleg.Id}/Roles/{(int)AdminRole.AdminManagement}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await db.UserInAdminRoles.AnyAsync(x => x.Role == AdminRole.AdminManagement));
    }

    [Fact]
    public async Task CannotRemoveLastAdminManagement()
    {
        // Arrange
        var (client, db) = await Client([AdminRole.AdminManagement]);

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Admins/{_oleg.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _oleg.Id));
    }

    /// <summary>Второй с <c>AdminManagement</c> есть — снимать её у себя можно</summary>
    [Fact]
    public async Task CanRevokeAdminManagementWhenAnotherHolderRemains()
    {
        // Arrange
        var (client, db) = await Client(
            [AdminRole.AdminManagement, AdminRole.Accounts],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.AdminManagement)));

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Admins/{_oleg.Id}/Roles/{(int)AdminRole.AdminManagement}");

        // Assert
        await response.ShouldBeSuccessful();
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _oleg.Id && x.Role == AdminRole.AdminManagement));
    }

    [Fact]
    public async Task CanRemoveAdmin()
    {
        // Arrange
        var (client, db) = await Client(
            [AdminRole.AdminManagement],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.AdminManagement, AdminRole.Accounts)));

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Admins/{_vasya.Id}");

        // Assert
        await response.ShouldBeSuccessful();
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _vasya.Id));
        Assert.True(await db.Profiles.AnyAsync(x => x.Id == _vasya.Id));
    }

    /// <summary>
    /// <c>AdminManagement</c> сняли, а токен ещё живёт свой час: роль из него отбрасывается
    /// на следующем же запросе, и эндпоинт его не пускает (3.3)
    /// </summary>
    [Fact]
    public async Task CannotRemoveAdminAfterOwnAdminManagementRevoked()
    {
        // Arrange
        var (client, db) = await Client(
            [AdminRole.Accounts],
            db => db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.AdminManagement, AdminRole.Accounts)),
            tokenRoles: [AdminRole.AdminManagement, AdminRole.Accounts]);

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Admins/{_vasya.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(2, await db.UserInAdminRoles.CountAsync(x => x.UserId == _vasya.Id));
    }

    /// <summary>Из токена остаются только роли, что ещё лежат в базе</summary>
    [Fact]
    public async Task TokenRolesAreTrimmedToCurrent()
    {
        // Arrange
        var (client, _) = await Client(
            [AdminRole.AdminManagement],
            tokenRoles: [AdminRole.AdminManagement, AdminRole.Accounts]);

        // Act
        var response = await client.GetAsync("/api/Admin/Me");

        // Assert
        await response.ShouldBeSuccessful();
        var me = await response.Content.ReadFromJsonAsync<AdminDto>();
        Assert.Equal([AdminRole.AdminManagement], me!.Roles);
    }

    /// <summary>Удалённый из админки со всё ещё живым токеном не проходит никуда</summary>
    [Fact]
    public async Task RemovedAdminTokenIsRejected()
    {
        // Arrange
        var (client, _) = await Client([], tokenRoles: [AdminRole.Accounts]);

        // Act
        var response = await client.GetAsync("/api/Admin/Me");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsNotFoundWithoutAdminManagement()
    {
        // Arrange
        var (client, _) = await Client([AdminRole.Accounts]);

        // Act
        var response = await client.GetAsync("/api/Admin/Admins");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(HttpClient client, ApplicationContext db)> Client(
        AdminRole[] roles,
        Action<ApplicationContext>? arrange = null,
        AdminRole[]? tokenRoles = null) =>
        await Given.AdminClient(_oleg.Id, tokenRoles ?? roles, db =>
        {
            db.Profiles.AddRange(_oleg, _vasya);
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, roles));
            arrange?.Invoke(db);
        });
}
