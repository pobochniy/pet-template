using System.Net;
using System.Net.Http.Json;
using PetTemplate.Shared.Dto;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Profile;

public class ProfileApiTests
{
    // ── Save ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_ValidProfile_ReturnsOk()
    {
        var (token, profile) = new UserBuilder().Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var dto = new ProfileDto
        {
            UserName = "NewName",
            Comment = "Hello",
            IsAdult = true,
            HasAcceptedTerms = true
        };

        var response = await client.PostAsJsonAsync("/api/Profile/Save", dto);

        await response.ShouldBeSuccessful();
    }

    [Fact]
    public async Task Save_UserNameTooShort_ReturnsUnprocessableEntity()
    {
        var (token, profile) = new UserBuilder().Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var dto = new ProfileDto { UserName = "x" };

        var response = await client.PostAsJsonAsync("/api/Profile/Save", dto);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Save_UserNameTooLong_ReturnsUnprocessableEntity()
    {
        var (token, profile) = new UserBuilder().Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var dto = new ProfileDto { UserName = new string('a', 21) };

        var response = await client.PostAsJsonAsync("/api/Profile/Save", dto);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Save_UserNameWhitespaceOnly_ReturnsUnprocessableEntity()
    {
        var (token, profile) = new UserBuilder().Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var dto = new ProfileDto { UserName = "   " };

        var response = await client.PostAsJsonAsync("/api/Profile/Save", dto);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Save_UserNamePaddedWithWhitespace_TrimsAndSaves()
    {
        var (token, profile) = new UserBuilder().Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(profile), token);

        var dto = new ProfileDto { UserName = "  ValidName  " };

        var response = await client.PostAsJsonAsync("/api/Profile/Save", dto);

        await response.ShouldBeSuccessful();
        db.ChangeTracker.Clear();
        var saved = await db.Profiles.FindAsync(profile.Id);
        Assert.Equal("ValidName", saved!.UserName);
    }

    [Fact]
    public async Task Save_Unauthorized_Returns401()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Profile/Save", new ProfileDto { UserName = "Alice" });

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

    [Fact]
    public async Task Details_Unauthorized_Returns401()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.GetAsync("/api/Profile/Details");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
