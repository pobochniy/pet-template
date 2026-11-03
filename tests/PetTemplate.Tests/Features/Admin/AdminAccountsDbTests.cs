using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Аккаунты в админке (спец. accounts, 8.5): поиск, карточка для проверки владельца,
/// удаление аккаунта без пространств, журнал. На базе — удаление держат внешние ключи,
/// поиск без учёта регистра — <c>citext</c>, а курсор журнала сравнивает id так, как
/// их сравнивает Postgres
/// </summary>
public class AdminAccountsDbTests
{
    private readonly AdminAccountsWorld _world = new();

    private Task<(HttpClient client, ApplicationContext db)> Client(Action<ApplicationContext>? extra = null) =>
        _world.Client(extra);

    [Theory]
    [InlineData("mash")]
    [InlineData("MASHA")]
    [InlineData("@masha")]
    [InlineData("masha@ponchiki")]
    [InlineData("MASHA@PONCHIKI.RU")]
    [InlineData("777001")]
    [InlineData("+79990000001")]
    public async Task FindsByLoginEmailPhoneOrTelegram(string query)
    {
        var (client, _) = await Client();

        var found = await client.GetFromJsonAsync<AdminAccountDto[]>($"/api/Admin/Accounts?query={Uri.EscapeDataString(query)}");

        Assert.Equal(_world.Masha.Id, Assert.Single(found!).UserId);
    }

    /// <summary>Подстановки <c>LIKE</c> в запросе — буквально: «_» не находит всех подряд</summary>
    [Fact]
    public async Task SearchTreatsWildcardsLiterally()
    {
        var (client, _) = await Client();

        var found = await client.GetFromJsonAsync<AdminAccountDto[]>("/api/Admin/Accounts?query=_");

        Assert.Empty(found!);
    }

    /// <summary>Карточка: пространства с участниками — без содержимого</summary>
    [Fact]
    public async Task CardShowsWorkspacesWithoutContents()
    {
        var petya = new UserBuilder("Petya")
            .WithPermissions(_world.Ponchiki.Id, PermissionEnum.WorkspaceMemberRead)
            .Please().profile;
        var (client, _) = await Client(x => x.Profiles.Add(petya));

        var card = await client.GetFromJsonAsync<AdminAccountCardDto>($"/api/Admin/Accounts/{_world.Masha.Id}");

        Assert.Equal("Masha", card!.Card.UserName);
        Assert.True(card.Card.HasTelegram);
        Assert.Equal("masha@ponchiki.ru", card.Email);
        Assert.False(card.IsAdmin);
        var workspace = Assert.Single(card.Card.Workspaces);
        Assert.Equal("Пончики и Плюшки", workspace.Name);
        Assert.True(workspace.IsOwner);
        Assert.Equal(["Petya"], workspace.OtherMembers);
    }

    [Fact]
    public async Task CardOfMissingAccountIsNotFound()
    {
        var (client, _) = await Client();

        var response = await client.GetAsync($"/api/Admin/Accounts/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Без роли <c>Accounts</c> — как будто раздела нет: 404 (спец. admin, 3.5)</summary>
    [Fact]
    public async Task WithoutRoleTheSectionDoesNotExist()
    {
        var world = new AdminAccountsWorld(AdminRole.AdminManagement);
        var (client, _) = await world.Client();

        var search = await client.GetAsync("/api/Admin/Accounts?query=masha");
        var card = await client.GetAsync($"/api/Admin/Accounts/{world.Masha.Id}");
        var journal = await client.GetAsync("/api/Admin/Accounts/Journal");

        Assert.Equal(HttpStatusCode.NotFound, search.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, card.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, journal.StatusCode);
    }

    /// <summary>Аккаунт без пространств удаляется с причиной в журнале (8.5)</summary>
    [Fact]
    public async Task DeletesAccountWithoutWorkspaces()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync($"/api/Admin/Accounts/{_world.Lonely.Id}/Delete",
            new AdminAccountReasonDto { Reason = "Забыл пароль, восстанавливать нечего" });

        await response.ShouldBeSuccessful();
        Assert.False(await db.Users.AnyAsync(x => x.Id == _world.Lonely.Id));
        var action = await db.AccountAdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(AccountAdminActionKind.Delete, action.Kind);
        Assert.Equal("Lonely", action.TargetUserName);
        Assert.Equal(_world.Admin.Id, action.AdminUserId);
    }

    /// <summary>У аккаунта есть пространства — его восстанавливают ссылкой, а не удаляют</summary>
    [Fact]
    public async Task DoesNotDeleteAccountWithWorkspaces()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync($"/api/Admin/Accounts/{_world.Masha.Id}/Delete",
            new AdminAccountReasonDto { Reason = "Попросили" });

        await response.ShouldBeBadRequest("У аккаунта есть пространства — его восстанавливают ссылкой сброса, а не удаляют");
        Assert.True(await db.Users.AnyAsync(x => x.Id == _world.Masha.Id));
    }

    /// <summary>Аккаунт с ролью в админке не удаляется: сначала роли снимает другой админ</summary>
    [Fact]
    public async Task DoesNotDeleteAdmin()
    {
        var (client, db) = await Client(x => x.UserInAdminRoles.Add(new UserInAdminRole
        {
            UserId = _world.Lonely.Id, Role = AdminRole.AdminManagement, GrantedUtc = DateTime.UtcNow
        }));

        var response = await client.PostAsJsonAsync($"/api/Admin/Accounts/{_world.Lonely.Id}/Delete",
            new AdminAccountReasonDto { Reason = "Попросили" });

        await response.ShouldBeBadRequestWith(AuthMessages.AdminCannotBeDeleted);
        Assert.True(await db.Users.AnyAsync(x => x.Id == _world.Lonely.Id));
        Assert.Empty(await db.AccountAdminActions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DeleteNeedsReason()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync($"/api/Admin/Accounts/{_world.Lonely.Id}/Delete",
            new AdminAccountReasonDto { Reason = " " });

        await response.ShouldBeBadRequest("Укажите причину");
        Assert.True(await db.Users.AnyAsync(x => x.Id == _world.Lonely.Id));
    }

    /// <summary>
    /// Карточка удалённого аккаунта — 404, а запись об удалении видна в журнале: с логином,
    /// id, админом и причиной (8.5, 11)
    /// </summary>
    [Fact]
    public async Task DeletedAccountStaysInJournal()
    {
        var (client, _) = await Client();
        var response = await client.PostAsJsonAsync($"/api/Admin/Accounts/{_world.Lonely.Id}/Delete",
            new AdminAccountReasonDto { Reason = "Сам попросил" });
        await response.ShouldBeSuccessful();

        var card = await client.GetAsync($"/api/Admin/Accounts/{_world.Lonely.Id}");
        var journal = await client.GetFromJsonAsync<AdminAccountJournalPageDto>("/api/Admin/Accounts/Journal");

        Assert.Equal(HttpStatusCode.NotFound, card.StatusCode);
        var entry = Assert.Single(journal!.Entries);
        Assert.Equal(AccountAdminActionKind.Delete, entry.Kind);
        Assert.Equal(_world.Lonely.Id, entry.TargetUserId);
        Assert.Equal("Lonely", entry.TargetUserName);
        Assert.False(entry.TargetExists);
        Assert.Equal("Boris", entry.AdminName);
        Assert.Equal("Сам попросил", entry.Reason);
        Assert.Null(journal.NextBefore);
    }

    /// <summary>Поиск — по части логина без учёта регистра или по id целиком</summary>
    [Theory]
    [InlineData("onel")]
    [InlineData("LONELY")]
    [InlineData("@lonely")]
    [InlineData("id")]
    public async Task JournalFindsByLoginOrId(string query)
    {
        var (client, db) = await Client();
        var now = DateTime.UtcNow;
        Act(db, _world.Lonely, AccountAdminActionKind.Delete, now);
        Act(db, _world.Masha, AccountAdminActionKind.IssueReset, now);
        await db.SaveChangesAsync();

        var text = query == "id" ? _world.Lonely.Id.ToString() : query;
        var journal = await client.GetFromJsonAsync<AdminAccountJournalPageDto>(
            $"/api/Admin/Accounts/Journal?query={Uri.EscapeDataString(text)}");

        Assert.Equal(_world.Lonely.Id, Assert.Single(journal!.Entries).TargetUserId);
    }

    /// <summary>
    /// Слияние в той же ленте: оба логина — копии из журнала; находится по логину и id
    /// любого из двух
    /// </summary>
    [Theory]
    [InlineData("masha_old")]
    [InlineData("MASHA")]
    [InlineData("removed")]
    [InlineData("kept")]
    public async Task JournalShowsMerges(string query)
    {
        var (client, db) = await Client();
        var removedId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        db.AccountMerges.Add(new AccountMerge
        {
            Id = Guid.CreateVersion7(),
            KeptUserId = _world.Masha.Id,
            KeptUserName = "Masha",
            RemovedUserId = removedId,
            RemovedUserName = "Masha_old",
            Platform = "telegram",
            CreatedUtc = now
        });
        Act(db, _world.Lonely, AccountAdminActionKind.IssueReset, now);
        await db.SaveChangesAsync();

        var text = query switch { "removed" => removedId.ToString(), "kept" => _world.Masha.Id.ToString(), _ => query };
        var journal = await client.GetFromJsonAsync<AdminAccountJournalPageDto>(
            $"/api/Admin/Accounts/Journal?query={Uri.EscapeDataString(text)}");

        var entry = Assert.Single(journal!.Entries);
        Assert.Null(entry.Kind);
        Assert.Equal(removedId, entry.TargetUserId);
        Assert.Equal("Masha_old", entry.TargetUserName);
        Assert.False(entry.TargetExists);
        Assert.Equal(_world.Masha.Id, entry.KeptUserId);
        Assert.Equal("Masha", entry.KeptUserName);
        Assert.Equal("telegram", entry.Platform);
    }

    /// <summary>
    /// Две таблицы — одна лента: новые сверху, страница за страницей без пропусков
    /// и повторов, в том числе на записях с одной меткой времени
    /// </summary>
    [Fact]
    public async Task JournalPagesThroughBothTables()
    {
        var (client, db) = await Client();
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 130; i++)
        {
            // Каждая пятая — с меткой соседней: курсор должен развести их по Id
            var at = start.AddMinutes(i - i % 5 / 4);
            if (i % 2 == 0) Act(db, _world.Masha, AccountAdminActionKind.IssueReset, at);
            else
            {
                db.AccountMerges.Add(new AccountMerge
                {
                    Id = Guid.CreateVersion7(),
                    KeptUserId = _world.Masha.Id,
                    KeptUserName = "Masha",
                    RemovedUserId = Guid.CreateVersion7(),
                    RemovedUserName = $"old{i}",
                    Platform = "vk",
                    CreatedUtc = at
                });
            }
        }
        await db.SaveChangesAsync();

        var all = new List<AdminAccountJournalEntryDto>();
        Guid? before = null;
        var pages = 0;
        do
        {
            var url = "/api/Admin/Accounts/Journal" + (before is { } cursor ? $"?before={cursor}" : "");
            var page = (await client.GetFromJsonAsync<AdminAccountJournalPageDto>(url))!;
            all.AddRange(page.Entries);
            before = page.NextBefore;
            pages++;
        } while (before != null && pages < 10);

        Assert.Equal(2, pages);
        Assert.Equal(130, all.Count);
        Assert.Equal(130, all.Select(x => x.Id).Distinct().Count());
        Assert.Equal(all.OrderByDescending(x => x.CreatedUtc).Select(x => x.CreatedUtc), all.Select(x => x.CreatedUtc));
    }

    private void Act(ApplicationContext db, PetTemplate.Db.Entity.Profile target, AccountAdminActionKind kind, DateTime at) =>
        db.AccountAdminActions.Add(new AccountAdminAction
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            AdminUserId = _world.Admin.Id,
            TargetUserId = target.Id,
            TargetUserName = target.UserName,
            Reason = "Проверка",
            CreatedUtc = at
        });
}
