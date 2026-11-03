using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Освобождение телефона (спец. accounts, 6): телефон он же логин, а подтверждения нет —
/// чужой номер вписывается раньше владельца. Поддержка снимает его с аккаунта, где он
/// лежит. На базе: снятие — условным <c>UPDATE</c>
/// </summary>
public class AdminPhoneReleaseDbTests
{
    private const string Phone = "+79990000005";

    private readonly AdminAccountsWorld _world = new();
    private readonly PetTemplate.Db.Entity.Profile _squatter = new UserBuilder("Squatter").WithContacts("squatter@ponchiki.ru", Phone).Please().profile;

    private Task<(HttpClient client, ApplicationContext db)> Client() =>
        _world.Client(x => x.Profiles.Add(_squatter));

    private static AdminReleasePhoneDto Release(string phone = Phone) => new()
    {
        Phone = phone,
        Reason = "Прислала фото договора с оператором"
    };

    private static Task<HttpResponseMessage> Post(HttpClient client, Guid userId, AdminReleasePhoneDto dto) =>
        client.PostAsJsonAsync($"/api/Admin/Accounts/{userId}/ReleasePhone", dto);

    [Fact]
    public async Task ReleasesPhone()
    {
        var (client, db) = await Client();

        // Пробелы вокруг номера не мешают найти его
        var response = await Post(client, _world.Lonely.Id, Release($" {Phone} "));

        await response.ShouldBeSuccessful();
        var released = await response.Content.ReadFromJsonAsync<AdminReleasedContactDto>();
        Assert.Equal("Squatter", released!.ReleasedFromUserName);

        var squatter = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == _squatter.Id);
        Assert.Null(squatter.PhoneNumber);
        Assert.Equal("squatter@ponchiki.ru", squatter.Email);

        var actions = await db.AccountAdminActions.AsNoTracking().ToListAsync();
        var mine = Assert.Single(actions, x => x.TargetUserId == _world.Lonely.Id);
        Assert.Equal(AccountAdminActionKind.ReleasePhone, mine.Kind);
        Assert.Contains("Squatter", mine.Reason);
        var theirs = Assert.Single(actions, x => x.TargetUserId == _squatter.Id);
        Assert.Equal(AccountAdminActionKind.PhoneReleased, theirs.Kind);
        Assert.Contains("Lonely", theirs.Reason);
    }

    [Fact]
    public async Task RefusesFreePhone()
    {
        var (client, db) = await Client();

        var response = await Post(client, _world.Lonely.Id, Release("+79990000099"));

        await AssertRefused(response, db);
    }

    [Fact]
    public async Task RefusesOwnPhone()
    {
        var (client, db) = await Client();

        var response = await Post(client, _squatter.Id, Release());

        await response.ShouldBeBadRequest("Этот телефон уже у этого аккаунта");
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
        Assert.Equal(Phone, (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == _squatter.Id)).PhoneNumber);
        Assert.Empty(await db.AccountAdminActions.AsNoTracking().ToListAsync());
    }
}
