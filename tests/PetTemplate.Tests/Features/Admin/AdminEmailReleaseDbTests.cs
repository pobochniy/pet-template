using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Освобождение почты (спец. accounts, 8.4 и 8.5): почта уникальна и она же логин,
/// поэтому чужой адрес можно вписать себе раньше владельца ящика. Поддержка снимает его
/// с аккаунта, где он не подтверждён. На базе: уникальный индекс по почте и <c>citext</c> —
/// сравнение без учёта регистра
/// </summary>
public class AdminEmailReleaseDbTests
{
    private const string Address = "olga@ponchiki.ru";

    private readonly AdminAccountsWorld _world = new();
    private readonly PetTemplate.Db.Entity.Profile _squatter = new UserBuilder("Squatter").WithContacts(Address, "+79990000005").Please().profile;

    private Task<(HttpClient client, ApplicationContext db)> Client(bool confirmed = false) =>
        _world.Client(x =>
        {
            _squatter.EmailConfirmed = confirmed;
            x.Profiles.Add(_squatter);
            var now = DateTime.UtcNow;
            x.AccountTokens.Add(new AccountToken
            {
                Id = Guid.CreateVersion7(),
                UserId = _squatter.Id,
                Kind = AccountTokenKind.EmailConfirm,
                Hash = AccountTokens.HashOf("confirm"),
                Email = Address,
                CreatedAtUtc = now.AddMinutes(-5),
                ExpiresAtUtc = now.AddDays(1)
            });
        });

    private static AdminReleaseEmailDto Release(string email = Address) => new()
    {
        Email = email,
        Reason = "Прислала скриншот ящика, назвала участников пространства"
    };

    private static Task<HttpResponseMessage> Post(HttpClient client, Guid userId, AdminReleaseEmailDto dto) =>
        client.PostAsJsonAsync($"/api/Admin/Accounts/{userId}/ReleaseEmail", dto);

    [Fact]
    public async Task ReleasesEmail()
    {
        var (client, db) = await Client();

        // Регистр и пробелы вокруг адреса не мешают найти его
        var response = await Post(client, _world.Lonely.Id, Release("  OLGA@ponchiki.ru "));

        await response.ShouldBeSuccessful();
        var released = await response.Content.ReadFromJsonAsync<AdminReleasedContactDto>();
        Assert.Equal("Squatter", released!.ReleasedFromUserName);

        var squatter = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == _squatter.Id);
        Assert.Null(squatter.Email);
        Assert.False(squatter.EmailConfirmed);
        Assert.NotNull((await db.AccountTokens.AsNoTracking().SingleAsync()).RevokedAtUtc);

        var actions = await db.AccountAdminActions.AsNoTracking().ToListAsync();
        var mine = Assert.Single(actions, x => x.TargetUserId == _world.Lonely.Id);
        Assert.Equal(AccountAdminActionKind.ReleaseEmail, mine.Kind);
        Assert.Contains("Squatter", mine.Reason);
        var theirs = Assert.Single(actions, x => x.TargetUserId == _squatter.Id);
        Assert.Equal(AccountAdminActionKind.EmailReleased, theirs.Kind);
        Assert.Contains("Lonely", theirs.Reason);

        // Адрес свободен — уникальный индекс больше не мешает вписать его владельцу
        var lonely = await db.Profiles.SingleAsync(x => x.Id == _world.Lonely.Id);
        lonely.Email = Address;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RefusesFreeAddress()
    {
        var (client, db) = await Client();

        var response = await Post(client, _world.Lonely.Id, Release("nobody@ponchiki.ru"));

        await AssertRefused(response, db);
    }

    [Fact]
    public async Task RefusesOwnAddress()
    {
        var (client, db) = await Client();

        var response = await Post(client, _squatter.Id, Release());

        await AssertRefused(response, db);
    }

    /// <summary>Подтверждённый адрес — ящик того аккаунта, а не захват: не снимается</summary>
    [Fact]
    public async Task RefusesConfirmedAddress()
    {
        var (client, db) = await Client(confirmed: true);

        var response = await Post(client, _world.Lonely.Id, Release());

        await AssertRefused(response, db);
    }

    [Fact]
    public async Task RefusesWithoutReason()
    {
        var (client, db) = await Client();
        var dto = Release();
        dto.Reason = null;

        var response = await Post(client, _world.Lonely.Id, dto);

        await AssertRefused(response, db);
    }

    [Fact]
    public async Task MissingAccountIsNotFound()
    {
        var (client, _) = await Client();

        var response = await Post(client, Guid.CreateVersion7(), Release());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task AssertRefused(HttpResponseMessage response, ApplicationContext db)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Address, (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == _squatter.Id)).Email);
        Assert.Null((await db.AccountTokens.AsNoTracking().SingleAsync()).RevokedAtUtc);
        Assert.Empty(await db.AccountAdminActions.AsNoTracking().ToListAsync());
    }
}
