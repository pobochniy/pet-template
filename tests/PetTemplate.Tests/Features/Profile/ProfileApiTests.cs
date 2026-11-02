using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Shared.Dto;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Profile;

public class ProfileApiTests
{
    // ── Save ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_ValidProfile_ReturnsSavedProfile()
    {
        var (token, profile) = new UserBuilder().Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var dto = new ProfileDto
        {
            IsAdult = true,
            HasAcceptedTerms = true
        };

        var response = await client.PostAsJsonAsync("/api/Profile/Save", dto);

        await response.ShouldBeSuccessful();
        var saved = await response.Content.ReadFromJsonAsync<ProfileDto>();
        Assert.True(saved!.IsAdult);
        Assert.True(saved.HasAcceptedTerms);
    }

    /// <summary>
    /// Логин и контакты <c>Profile/Save</c> не пишет (спец. accounts, 4.3 и 6): у них свои
    /// вызовы и отказы «занят». Раньше занятый логин здесь молча подменялся на логин с GUID
    /// </summary>
    [Fact]
    public async Task Save_DoesNotChangeUserNameOrContacts()
    {
        var (token, profile) = new UserBuilder("Oleg").WithContacts("oleg@example.com", "+79990000001").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var response = await client.PostAsJsonAsync("/api/Profile/Save", new ProfileDto
        {
            UserName = "Vasya",
            Email = "vasya@example.com",
            Phone = "+79990000002",
            IsAdult = true
        });

        await response.ShouldBeSuccessful();
        var saved = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == profile.Id);
        Assert.Equal("Oleg", saved.UserName);
        Assert.Equal("oleg@example.com", saved.Email);
        Assert.Equal("+79990000001", saved.PhoneNumber);
    }

    [Fact]
    public async Task Save_Unauthorized_Returns401()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Profile/Save", new ProfileDto { IsAdult = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── SetAvatar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAvatar_ValidId_ReturnsOk()
    {
        var (token, profile) = new UserBuilder().Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var response = await client.PostAsJsonAsync("/api/Profile/SetAvatar", new SetAvatarDto { AvatarId = 5 });

        await response.ShouldBeSuccessful();
    }

    [Fact]
    public async Task SetAvatar_IdExceedsRange_ReturnsUnprocessableEntity()
    {
        var (token, profile) = new UserBuilder().Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var response = await client.PostAsJsonAsync("/api/Profile/SetAvatar", new SetAvatarDto { AvatarId = 10000 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task SetAvatar_Unauthorized_Returns401()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Profile/SetAvatar", new SetAvatarDto { AvatarId = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Details ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Details_ReturnsCurrentUserProfile()
    {
        var (token, profile) = new UserBuilder("TestUser").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var response = await client.GetAsync("/api/Profile/Details");

        await response.ShouldBeSuccessful();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("TestUser", json);
    }

    /// <summary>
    /// Наружу едет <see cref="ProfileDto"/>, а не сущность: ни хеша, ни соли, ни связей
    /// (<c>ProfileMapping</c>) — профиль клиент кладёт в <c>localStorage</c>
    /// </summary>
    [Fact]
    public async Task Details_DoesNotLeakPasswordHash()
    {
        var (token, profile) = new UserBuilder("TestUser").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var response = await client.GetAsync("/api/Profile/Details");

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
        var details = await response.Content.ReadFromJsonAsync<ProfileDto>();
        Assert.True(details!.HasPassword);
    }

    [Fact]
    public async Task Details_Unauthorized_Returns401()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.GetAsync("/api/Profile/Details");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
