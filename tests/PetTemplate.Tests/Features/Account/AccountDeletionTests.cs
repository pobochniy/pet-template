using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Account;

/// <summary>
/// Удаление аккаунта (спец. accounts, разделы 9 и 10). Обнуление авторства и каскад личного
/// держат внешние ключи в базе — поэтому проверяется на PostgreSQL, а не на словах.
///
/// Олег — владелец «Рогов и Копыт», где с ним Вася (только чтение состава), и один
/// в «Шариках и Палочках». В «Рогах» он выдал приглашение, по которому пришёл Вася, и сам
/// пришёл по Васиному; в «Шариках» висит его живое приглашение. У Олега — токен сброса пароля.
/// Авторство в ядре — только у приглашений (<c>CreatedBy</c>, <c>AcceptedBy</c>), личное —
/// токены аккаунта: на них и проверяется правило «авторство — обнуление, личное — каскад»
/// </summary>
public class AccountDeletionTests
{
    private readonly PetTemplate.Db.Entity.Workspace _roga = new WorkspaceBuilder("Рога и Копыта").Please();
    private readonly PetTemplate.Db.Entity.Workspace _shariki = new WorkspaceBuilder("Шарики и Палочки").Please();
    private readonly string _olegToken;
    private readonly PetTemplate.Db.Entity.Profile _oleg;
    private readonly PetTemplate.Db.Entity.Profile _vasya;
    private readonly WorkspaceInvitation _invitedVasya;
    private readonly WorkspaceInvitation _invitedOleg;
    private readonly WorkspaceInvitation _soloInvitation;
    private readonly AccountToken _reset;

    public AccountDeletionTests()
    {
        (_olegToken, _oleg) = new UserBuilder("Oleg")
            .WithContacts("oleg@roga-i-kopyta.ru", "+79990000001")
            .WithAllPermissions(_roga.Id)
            .WithAllPermissions(_shariki.Id)
            .WithPrimaryWorkspace(_roga.Id)
            .Please();
        (_, _vasya) = new UserBuilder("Vasya")
            .WithPermissions(_roga.Id, PermissionEnum.WorkspaceMemberRead)
            .WithPrimaryWorkspace(_roga.Id)
            .Please();

        _invitedVasya = new WorkspaceInvitationBuilder(_roga.Id, _oleg.Id).Accepted(_vasya.Id).Please();
        _invitedOleg = new WorkspaceInvitationBuilder(_roga.Id, _vasya.Id).Accepted(_oleg.Id).Please();
        _soloInvitation = new WorkspaceInvitationBuilder(_shariki.Id, _oleg.Id).Please();
        var created = DateTime.UtcNow;
        _reset = new AccountToken
        {
            Id = Guid.CreateVersion7(),
            UserId = _oleg.Id,
            Kind = AccountTokenKind.PasswordReset,
            Hash = AccountTokens.HashOf("reset-token-of-oleg"),
            Email = "oleg@roga-i-kopyta.ru",
            CreatedAtUtc = created,
            ExpiresAtUtc = created.AddHours(1)
        };
    }

    private void World(ApplicationContext x)
    {
        x.Workspaces.AddRange(_roga, _shariki);
        x.Profiles.AddRange(_oleg, _vasya);
        x.WorkspaceInvitations.AddRange(_invitedVasya, _invitedOleg, _soloInvitation);
        x.AccountTokens.Add(_reset);
    }

    private Task<(HttpClient client, ApplicationContext db)> Client() => Given.ApiClient(World, _olegToken);

    /// <summary>Подтверждение — свой логин в другом регистре: сравнение без учёта регистра (4.1)</summary>
    private async Task<(HttpClient client, ApplicationContext db)> DeleteOleg()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Account/Delete", new DeleteAccountDto { UserName = " oleg " });
        await response.ShouldBeSuccessful();

        db.ChangeTracker.Clear();
        return (client, db);
    }

    [Fact]
    public async Task UserAndProfileAreGone()
    {
        var (_, db) = await DeleteOleg();

        Assert.False(await db.Users.AnyAsync(x => x.Id == _oleg.Id));
        Assert.False(await db.Profiles.AnyAsync(x => x.Id == _oleg.Id));
        Assert.False(await db.UserInPermissions.AnyAsync(x => x.UserId == _oleg.Id));
    }

    /// <summary>Токен удалённого не действует: проверка сессии не находит пользователя</summary>
    [Fact]
    public async Task TokenStopsWorking()
    {
        var (client, _) = await DeleteOleg();

        var response = await client.GetAsync("/api/Account/Logins");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Общее пространство остаётся Васе — выход по правилам спец. authorization, 8: оставшийся
    /// один получает все права, и пространство не остаётся без того, кто раздаёт права
    /// </summary>
    [Fact]
    public async Task SharedWorkspaceStaysWithRightsPassed()
    {
        var (_, db) = await DeleteOleg();

        Assert.True(await db.Workspaces.AnyAsync(x => x.Id == _roga.Id));
        var rights = await db.UserInPermissions.Where(x => x.WorkspaceId == _roga.Id && x.UserId == _vasya.Id)
            .Select(x => x.PermissionId).ToListAsync();
        Assert.Equal(Enum.GetValues<PermissionEnum>().Length - 1, rights.Count);
    }

    /// <summary>Пространство, где он один, удаляется сразу — вместе со своими приглашениями (раздел 10)</summary>
    [Fact]
    public async Task SoloWorkspaceIsDeletedWithContents()
    {
        var (_, db) = await DeleteOleg();

        Assert.False(await db.Workspaces.AnyAsync(x => x.Id == _shariki.Id));
        Assert.False(await db.WorkspaceInvitations.AnyAsync(x => x.Id == _soloInvitation.Id));
    }

    /// <summary>
    /// Авторство в общем пространстве — обнулением: приглашения остаются, ссылка на Олега
    /// пустеет, Васина — на месте
    /// </summary>
    [Fact]
    public async Task InvitationAuthorshipIsCleared()
    {
        var (_, db) = await DeleteOleg();

        var invitedVasya = await db.WorkspaceInvitations.SingleAsync(x => x.Id == _invitedVasya.Id);
        Assert.Null(invitedVasya.CreatedBy);
        Assert.Equal(_vasya.Id, invitedVasya.AcceptedBy);
        var invitedOleg = await db.WorkspaceInvitations.SingleAsync(x => x.Id == _invitedOleg.Id);
        Assert.Equal(_vasya.Id, invitedOleg.CreatedBy);
        Assert.Null(invitedOleg.AcceptedBy);
    }

    /// <summary>Личное — каскадом: ссылка сброса удалённого не живёт дальше без хозяина</summary>
    [Fact]
    public async Task PersonalTokensAreDeleted()
    {
        var (_, db) = await DeleteOleg();

        Assert.False(await db.AccountTokens.AnyAsync(x => x.Id == _reset.Id));
    }

    /// <summary>Основное пространство Васи не тронуто: он из него не уходил</summary>
    [Fact]
    public async Task OthersPrimaryWorkspaceStays()
    {
        var (_, db) = await DeleteOleg();

        Assert.Equal(_roga.Id, (await db.Profiles.SingleAsync(x => x.Id == _vasya.Id)).PrimaryWorkspaceId);
    }

    /// <summary>Логин, почта и телефон освобождаются сразу — можно зарегистрироваться заново</summary>
    [Fact]
    public async Task LoginIsFreeForNewRegistration()
    {
        var (client, _) = await DeleteOleg();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = "Oleg",
            Email = "oleg@roga-i-kopyta.ru",
            Phone = "+79990000001",
            Password = UserBuilder.SuperAdminPassword,
            PasswordConfirm = UserBuilder.SuperAdminPassword
        });

        await response.ShouldBeSuccessful();
    }

    [Fact]
    public async Task WrongConfirmationKeepsAccount()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Account/Delete", new DeleteAccountDto { UserName = "Vasya" });

        await response.ShouldBeBadRequestWith(AuthMessages.DeleteConfirmMismatch);
        Assert.True(await db.Users.AnyAsync(x => x.Id == _oleg.Id));
        Assert.True(await db.Workspaces.AnyAsync(x => x.Id == _shariki.Id));
    }

    /// <summary>Аккаунт с ролью в админке не удаляется — сначала роли снимает другой админ (раздел 10)</summary>
    [Fact]
    public async Task AdminCannotDeleteThemselves()
    {
        var (client, db) = await Given.ApiClient(x =>
        {
            World(x);
            x.UserInAdminRoles.Add(new UserInAdminRole
            {
                UserId = _oleg.Id, Role = AdminRole.AdminManagement, GrantedUtc = DateTime.UtcNow
            });
        }, _olegToken);

        var response = await client.PostAsJsonAsync("/api/Account/Delete", new DeleteAccountDto { UserName = "Oleg" });

        await response.ShouldBeBadRequestWith(AuthMessages.AdminCannotBeDeleted);
        Assert.True(await db.Users.AnyAsync(x => x.Id == _oleg.Id));
        Assert.True(await db.Workspaces.AnyAsync(x => x.Id == _shariki.Id));
    }

    /// <summary>Карточка удаления говорит, что аккаунт с ролью удалить нельзя (7.3)</summary>
    [Fact]
    public async Task PreviewTellsAdminCannotBeRemoved()
    {
        var (client, _) = await Given.ApiClient(x =>
        {
            World(x);
            x.UserInAdminRoles.Add(new UserInAdminRole
            {
                UserId = _oleg.Id, Role = AdminRole.AdminManagement, GrantedUtc = DateTime.UtcNow
            });
        }, _olegToken);

        var card = await client.GetFromJsonAsync<AccountCardDto>("/api/Account/DeletePreview");

        Assert.False(card!.CanBeRemoved);
    }

    /// <summary>Экран удаления видит, какое пространство удалится, а какое останется (раздел 9)</summary>
    [Fact]
    public async Task PreviewTellsWhatGoesAway()
    {
        var (client, _) = await Client();

        var card = await client.GetFromJsonAsync<AccountCardDto>("/api/Account/DeletePreview");

        Assert.Equal("Oleg", card!.UserName);
        Assert.True(card.Workspaces.Single(x => x.Id == _shariki.Id).IsSolo);
        var roga = card.Workspaces.Single(x => x.Id == _roga.Id);
        Assert.False(roga.IsSolo);
        Assert.True(roga.IsOwner);
        Assert.Equal(["Vasya"], roga.OtherMembers);
    }

    /// <summary>
    /// Память ассистента: личное Олега уходит каскадом, общий факт общего пространства
    /// остаётся Васе, память пространства, где Олег был один, уносит снос (спец. assistant, 12.5)
    /// </summary>
    [Fact]
    public async Task PersonalFactsAreDeletedSharedStay()
    {
        var personal = new AssistantFactBuilder(_roga.Id, "встречи", "встречи обычно на час").Of(_oleg.Id).Please();
        var shared = new AssistantFactBuilder(_roga.Id, "илья", "«Илья» — это Илья Петров").Please();
        var solo = new AssistantFactBuilder(_shariki.Id, "домик", "«домик» — это дача").Please();
        var (client, db) = await Given.ApiClient(x =>
        {
            World(x);
            x.AssistantFacts.AddRange(personal, shared, solo);
        }, _olegToken);

        var response = await client.PostAsJsonAsync("/api/Account/Delete", new DeleteAccountDto { UserName = "oleg" });

        await response.ShouldBeSuccessful();
        var left = await db.AssistantFacts.AsNoTracking().Select(x => x.Id).ToListAsync();
        Assert.Equal([shared.Id], left);
    }
}
