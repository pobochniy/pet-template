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
/// Приглашения админов (спец. admin, 3.4): выдача не больше своих ролей, страница
/// по токену, принятие логином от продукта сразу с админским токеном — и отказ
/// отозванному, просроченному и уже принятому
/// </summary>
public class AdminInvitationsApiTests
{
    private readonly PetTemplate.Db.Entity.Profile _oleg = new UserBuilder("Oleg").Please().profile;

    private readonly PetTemplate.Db.Entity.Profile _vasya = new UserBuilder("Vasya").WithContacts("vasya@example.com", "+79990000021").Please().profile;

    [Fact]
    public async Task CanInviteWithOwnRoles()
    {
        // Arrange
        var (client, db) = await Client();

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Invitations",
            new AdminInvitationCreateDto { Roles = [AdminRole.AdminManagement, AdminRole.Accounts] });

        // Assert
        await response.ShouldBeSuccessful();
        var invitation = await response.Content.ReadFromJsonAsync<AdminInvitationIssuedDto>();
        Assert.Equal([AdminRole.AdminManagement, AdminRole.Accounts], invitation!.Roles);
        Assert.Equal(AdminInvitationStatus.Pending, invitation.Status);
        Assert.Equal("Oleg", invitation.CreatedByName);
        Assert.InRange(invitation.ExpiresUtc - invitation.CreatedUtc, TimeSpan.FromDays(3), TimeSpan.FromDays(3));

        var stored = await db.AdminInvitations.AsNoTracking().SingleAsync();
        Assert.Equal(AdminRole.AdminManagement | AdminRole.Accounts, stored.Roles);
        Assert.Equal(4, invitation.Token.Version);
        Assert.Equal(AdminInvitation.HashOf(invitation.Token), stored.TokenHash);
    }

    /// <summary>
    /// В списке ссылок нет: иначе любой с <c>AdminManagement</c> принял бы чужую ссылку
    /// сам и получил роль, которой у него нет
    /// </summary>
    [Fact]
    public async Task ListDoesNotExposeTokens()
    {
        // Arrange
        var (client, _) = await Client();
        var issued = await client.PostAsJsonAsync("/api/Admin/Invitations",
            new AdminInvitationCreateDto { Roles = [AdminRole.Accounts] });
        var token = (await issued.Content.ReadFromJsonAsync<AdminInvitationIssuedDto>())!.Token;

        // Act
        var response = await client.GetAsync("/api/Admin/Invitations");

        // Assert
        await response.ShouldBeSuccessful();
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(token.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"token\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CannotInviteWithRoleNotOwned()
    {
        // Arrange
        var (client, db) = await Client(roles: [AdminRole.AdminManagement]);

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Invitations",
            new AdminInvitationCreateDto { Roles = [AdminRole.AdminManagement, AdminRole.Accounts] });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await db.AdminInvitations.AnyAsync());
    }

    [Fact]
    public async Task CannotInviteWithoutRoles()
    {
        // Arrange
        var (client, db) = await Client();

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Invitations", new AdminInvitationCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await db.AdminInvitations.AnyAsync());
    }

    [Fact]
    public async Task CanSeeInvitationsWithStatuses()
    {
        // Arrange
        var (client, _) = await Client(db => db.AdminInvitations.AddRange(
            Invitation(AdminRole.Accounts).invitation,
            Invitation(AdminRole.Accounts, createdDaysAgo: 4).invitation,
            Invitation(AdminRole.Accounts, AdminInvitationStatus.Revoked, createdDaysAgo: 1).invitation));

        // Act
        var response = await client.GetAsync("/api/Admin/Invitations");

        // Assert
        await response.ShouldBeSuccessful();
        var invitations = await response.Content.ReadFromJsonAsync<AdminInvitationDto[]>();
        Assert.Equal(
            [AdminInvitationStatus.Pending, AdminInvitationStatus.Revoked, AdminInvitationStatus.Expired],
            invitations!.Select(x => x.Status));
    }

    [Fact]
    public async Task CanRevokePendingInvitation()
    {
        // Arrange
        var (invitation, _) = Invitation(AdminRole.Accounts);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Invitations/{invitation.Id}");

        // Assert
        await response.ShouldBeSuccessful();
        var stored = await db.AdminInvitations.AsNoTracking().SingleAsync();
        Assert.Equal(AdminInvitationStatus.Revoked, stored.Status);
    }

    /// <summary>Отозвать можно и просроченное (3.4): вреда от этого нет, а в списке понятнее</summary>
    [Fact]
    public async Task CanRevokeExpiredInvitation()
    {
        // Arrange
        var (invitation, _) = Invitation(AdminRole.Accounts, createdDaysAgo: 4);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Invitations/{invitation.Id}");

        // Assert
        await response.ShouldBeSuccessful();
        Assert.Equal(AdminInvitationStatus.Revoked, (await db.AdminInvitations.AsNoTracking().SingleAsync()).Status);
    }

    /// <summary>
    /// <c>AdminManagement</c> сняли, а токен ещё живёт свой час: роль из него отбрасывается
    /// на следующем же запросе, и приглашать он уже не может (3.3)
    /// </summary>
    [Fact]
    public async Task CannotInviteAfterOwnAdminManagementRevoked()
    {
        // Arrange
        var (client, db) = await Client(db => db.UserInAdminRoles.RemoveRange(
            db.UserInAdminRoles.Local.Where(x => x.Role == AdminRole.AdminManagement).ToList()));

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Invitations",
            new AdminInvitationCreateDto { Roles = [AdminRole.Accounts] });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.AdminInvitations.AnyAsync());
    }

    /// <summary>
    /// Перебор токенов страницы приглашения запирает и вход с того же адреса (3.4):
    /// счётчик у обеих дверей общий, иначе вход подбирали бы через вторую
    /// </summary>
    [Fact]
    public async Task GuessingInvitationTokensLocksLoginFromSameAddress()
    {
        // Arrange
        var (client, _) = await Client(attempts: 2);
        client.DefaultRequestHeaders.Authorization = null;
        await client.GetAsync($"/api/Admin/Invitations/{Guid.NewGuid()}");
        await client.GetAsync($"/api/Admin/Invitations/{Guid.NewGuid()}");

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Login", Login("Oleg"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Отзыв пишет, кто отозвал (3.5), и список показывает его имя</summary>
    [Fact]
    public async Task RevokeRemembersWhoRevoked()
    {
        // Arrange
        var (invitation, _) = Invitation(AdminRole.Accounts);
        var (client, _) = await Client(db => db.AdminInvitations.Add(invitation));
        await client.DeleteAsync($"/api/Admin/Invitations/{invitation.Id}");

        // Act
        var response = await client.GetAsync("/api/Admin/Invitations");

        // Assert
        await response.ShouldBeSuccessful();
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<AdminInvitationDto[]>())!);
        Assert.Equal("Oleg", listed.RevokedByName);
    }

    /// <summary>У принявшего роли, и убираются они снятием ролей, а не отзывом ссылки</summary>
    [Fact]
    public async Task CannotRevokeAcceptedInvitation()
    {
        // Arrange
        var (invitation, _) = Invitation(AdminRole.Accounts, AdminInvitationStatus.Accepted);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Invitations/{invitation.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AdminInvitationStatus.Accepted, (await db.AdminInvitations.AsNoTracking().SingleAsync()).Status);
    }

    /// <summary>Страница приглашения до входа: кто позвал и какие роли</summary>
    [Fact]
    public async Task CanPreviewInvitationAnonymously()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.AdminManagement | AdminRole.Accounts);
        var (client, _) = await Client(db => db.AdminInvitations.Add(invitation));
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync($"/api/Admin/Invitations/{token}");

        // Assert
        await response.ShouldBeSuccessful();
        var preview = await response.Content.ReadFromJsonAsync<AdminInvitationPreviewDto>();
        Assert.Equal(AdminInvitationStatus.Pending, preview!.Status);
        Assert.Equal("Oleg", preview.InviterName);
        Assert.Equal([AdminRole.AdminManagement, AdminRole.Accounts], preview.Roles);
    }

    /// <summary>
    /// Страницу открывает всякий, у кого есть ссылка: почта и телефон пригласившего
    /// ему не показываются — только логин
    /// </summary>
    [Fact]
    public async Task PreviewDoesNotExposeInviterContacts()
    {
        // Arrange
        _oleg.Email = "oleg@roga-i-kopyta.ru";
        _oleg.PhoneNumber = "+79091112244";
        var (invitation, token) = Invitation(AdminRole.Accounts);
        var (client, _) = await Client(db => db.AdminInvitations.Add(invitation));
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync($"/api/Admin/Invitations/{token}");

        // Assert
        await response.ShouldBeSuccessful();
        var preview = await response.Content.ReadFromJsonAsync<AdminInvitationPreviewDto>();
        Assert.Equal(_oleg.UserName, preview!.InviterName);
    }

    /// <summary>У отработавшего — только статус: рассказывать о нём нечего</summary>
    [Fact]
    public async Task PreviewOfRevokedInvitationHasNoDetails()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.Accounts, AdminInvitationStatus.Revoked);
        var (client, _) = await Client(db => db.AdminInvitations.Add(invitation));
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync($"/api/Admin/Invitations/{token}");

        // Assert
        await response.ShouldBeSuccessful();
        var preview = await response.Content.ReadFromJsonAsync<AdminInvitationPreviewDto>();
        Assert.Equal(AdminInvitationStatus.Revoked, preview!.Status);
        Assert.Null(preview.InviterName);
        Assert.Empty(preview.Roles);
    }

    [Fact]
    public async Task ReturnsNotFoundOnPreviewOfUnknownToken()
    {
        // Arrange
        var (client, _) = await Client();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync($"/api/Admin/Invitations/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Принятие логином и паролем от продукта: роли из приглашения, отметка «принято»
    /// и сразу админский токен — второй вход не нужен
    /// </summary>
    [Fact]
    public async Task CanAcceptInvitation()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.AdminManagement | AdminRole.Accounts);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Vasya"));

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AdminLoginResultDto>();
        Assert.False(string.IsNullOrEmpty(result!.Token));
        Assert.Equal(_vasya.Id, result.Admin.UserId);
        Assert.Equal([AdminRole.AdminManagement, AdminRole.Accounts], result.Admin.Roles);

        var roles = await db.UserInAdminRoles.AsNoTracking().Where(x => x.UserId == _vasya.Id).ToListAsync();
        Assert.Equal(2, roles.Count);
        Assert.All(roles, x => Assert.Equal(_oleg.Id, x.GrantedByUserId));

        var stored = await db.AdminInvitations.AsNoTracking().SingleAsync();
        Assert.Equal(AdminInvitationStatus.Accepted, stored.Status);
        Assert.Equal(_vasya.Id, stored.AcceptedByUserId);
    }

    /// <summary>
    /// Ссылка выдаёт не больше, чем у выдавшего есть сейчас: снятая с него роль через
    /// неё не приходит
    /// </summary>
    [Fact]
    public async Task AcceptGrantsOnlyRolesInviterStillHas()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.AdminManagement | AdminRole.Accounts);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));
        db.UserInAdminRoles.RemoveRange(db.UserInAdminRoles.Where(x => x.UserId == _oleg.Id && x.Role == AdminRole.Accounts));
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Vasya"));

        // Assert
        await response.ShouldBeSuccessful();
        var roles = await db.UserInAdminRoles.AsNoTracking().Where(x => x.UserId == _vasya.Id).Select(x => x.Role).ToListAsync();
        Assert.Equal([AdminRole.AdminManagement], roles);
    }

    /// <summary>Выдавший больше не управляет админами — его ссылки не работают</summary>
    [Fact]
    public async Task CannotAcceptInvitationOfRemovedManager()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.Accounts);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));
        db.UserInAdminRoles.RemoveRange(db.UserInAdminRoles.Where(x => x.UserId == _oleg.Id && x.Role == AdminRole.AdminManagement));
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Vasya"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _vasya.Id));
        Assert.Equal(AdminInvitationStatus.Pending, (await db.AdminInvitations.AsNoTracking().SingleAsync()).Status);
    }

    /// <summary>Выданный токен сразу открывает админку — тем же путём, что после входа</summary>
    [Fact]
    public async Task AcceptedTokenOpensAdmin()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.Accounts);
        var (client, _) = await Client(db => db.AdminInvitations.Add(invitation));
        client.DefaultRequestHeaders.Authorization = null;
        var accepted = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Vasya"));
        var adminToken = (await accepted.Content.ReadFromJsonAsync<AdminLoginResultDto>())!.Token;
        client.DefaultRequestHeaders.Authorization = new("Bearer", adminToken);

        // Act
        var response = await client.GetAsync("/api/Admin/Me");

        // Assert
        await response.ShouldBeSuccessful();
        Assert.Equal([AdminRole.Accounts], (await response.Content.ReadFromJsonAsync<AdminDto>())!.Roles);
    }

    [Fact]
    public async Task CannotAcceptRevokedInvitation()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.Accounts, AdminInvitationStatus.Revoked);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Vasya"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _vasya.Id));
    }

    [Fact]
    public async Task CannotAcceptExpiredInvitation()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.Accounts, createdDaysAgo: 4);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Vasya"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _vasya.Id));
    }

    /// <summary>Ссылка одноразовая: второй, пришедший по ней, ролей не получает</summary>
    [Fact]
    public async Task CannotAcceptInvitationTwice()
    {
        // Arrange
        var petya = new UserBuilder("Petya").WithContacts("petya@example.com", "+79990000022").Please().profile;
        var (invitation, token) = Invitation(AdminRole.Accounts);
        var (client, db) = await Client(db =>
        {
            db.Profiles.Add(petya);
            db.AdminInvitations.Add(invitation);
        });
        client.DefaultRequestHeaders.Authorization = null;
        await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Vasya"));

        // Act
        var response = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Petya"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.UserId == petya.Id));
    }

    [Fact]
    public async Task CannotAcceptWithWrongPassword()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.Accounts);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation));
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept",
            Login("Vasya", "wrong-password"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(AdminInvitationStatus.Pending, (await db.AdminInvitations.AsNoTracking().SingleAsync()).Status);
    }

    /// <summary>Вторая дверь запирается так же, как <c>Admin/Login</c>: исчерпан лимит — не пускает и с верным паролем</summary>
    [Fact]
    public async Task LocksAcceptAfterTooManyFailures()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.Accounts);
        var (client, db) = await Client(db => db.AdminInvitations.Add(invitation), attempts: 2);
        client.DefaultRequestHeaders.Authorization = null;
        var url = $"/api/Admin/Invitations/{token}/Accept";
        await client.PostAsJsonAsync(url, Login("Vasya", "wrong-password"));
        await client.PostAsJsonAsync(url, Login("Vasya", "wrong-password"));

        // Act
        var response = await client.PostAsJsonAsync(url, Login("Vasya"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.UserInAdminRoles.AnyAsync(x => x.UserId == _vasya.Id));
    }

    /// <summary>Уже админу приглашение дописывает недостающие роли, имеющиеся не трогает</summary>
    [Fact]
    public async Task AcceptByAdminAddsMissingRoles()
    {
        // Arrange
        var (invitation, token) = Invitation(AdminRole.AdminManagement | AdminRole.Accounts);
        var (client, db) = await Client(db =>
        {
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_vasya.Id, AdminRole.Accounts));
            db.AdminInvitations.Add(invitation);
        });
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync($"/api/Admin/Invitations/{token}/Accept", Login("Vasya"));

        // Assert
        await response.ShouldBeSuccessful();
        var roles = await db.UserInAdminRoles.AsNoTracking().Where(x => x.UserId == _vasya.Id).ToListAsync();
        Assert.Equal([AdminRole.AdminManagement, AdminRole.Accounts], roles.Select(x => x.Role).Order());
        Assert.Null(roles.Single(x => x.Role == AdminRole.Accounts).GrantedByUserId);
    }

    /// <summary>Строка с хешем и сам токен — он есть только у того, кто выдал ссылку</summary>
    private (AdminInvitation invitation, Guid token) Invitation(
        AdminRole roles,
        AdminInvitationStatus status = AdminInvitationStatus.Pending,
        int createdDaysAgo = 0)
    {
        var created = DateTime.UtcNow.AddDays(-createdDaysAgo).AddMinutes(-1);
        var token = Guid.NewGuid();

        return (new AdminInvitation
        {
            Id = Guid.CreateVersion7(),
            TokenHash = AdminInvitation.HashOf(token),
            Roles = roles,
            Status = status,
            CreatedUtc = created,
            ExpiresUtc = created.AddDays(3),
            CreatedByUserId = _oleg.Id,
            // Принявший — живой аккаунт: на него ссылается внешний ключ
            AcceptedByUserId = status == AdminInvitationStatus.Accepted ? _vasya.Id : null
        }, token);
    }

    /// <summary>Олег — админ с <paramref name="roles"/> (по умолчанию обе) в базе и в токене</summary>
    private async Task<(HttpClient client, ApplicationContext db)> Client(
        Action<ApplicationContext>? arrange = null,
        int? attempts = null,
        AdminRole[]? roles = null)
    {
        roles ??= [AdminRole.AdminManagement, AdminRole.Accounts];

        return await Given.AdminClient(_oleg.Id, roles, db =>
            {
                db.Profiles.AddRange(_oleg, _vasya);
                db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, roles));
                arrange?.Invoke(db);
            },
            attempts == null ? null : new Dictionary<string, string?> { ["AdminOptions:AdminLoginAttempts"] = attempts.ToString() });
    }

    private static LoginDto Login(string login, string? password = null) =>
        new() { Login = login, Password = password ?? UserBuilder.SuperAdminPassword };
}
