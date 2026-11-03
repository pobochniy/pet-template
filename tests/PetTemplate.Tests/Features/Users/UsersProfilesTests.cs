using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Users;

/// <summary>
/// Состав пространства — <c>Users/GetProfiles</c> (спец. authorization, 5): видит любой
/// участник и только он, без контактов
/// </summary>
public class UsersProfilesTests
{
    [Fact]
    public async Task MemberSeesOnlyMembersOfOwnWorkspace()
    {
        var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
        var neighbours = new WorkspaceBuilder("Шарики и Палочки").Please();
        var (_, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();
        var (_, petya) = new UserBuilder("Petya")
            .WithAllPermissions(neighbours.Id)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, neighbours);
            db.Profiles.AddRange(oleg, vasya, petya);
        }, token, workspace.Id);

        var response = await client.GetAsync("/api/Users/GetProfiles");

        await response.ShouldBeSuccessful();
        var profiles = await response.Content.ReadFromJsonAsync<List<ProfileSummaryDto>>();
        Assert.Equal(["Oleg", "Vasya"], profiles!.Select(x => x.UserName).Order());
    }

    /// <summary>
    /// Почта и телефон участника — только в его собственном профиле: в ответе списка
    /// этих полей нет вовсе, а не просто пусто
    /// </summary>
    [Fact]
    public async Task MembersListHasNoContacts()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (_, vasya) = new UserBuilder("Vasya")
            .WithContacts("vasya@example.com", "+79991234567")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);

        var response = await client.GetAsync("/api/Users/GetProfiles");

        await response.ShouldBeSuccessful();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("vasya@example.com", body);
        Assert.DoesNotContain("+79991234567", body);

        var vasyaRow = JsonDocument.Parse(body).RootElement.EnumerateArray()
            .Single(x => x.GetProperty("userName").GetString() == "Vasya");
        Assert.False(vasyaRow.TryGetProperty("email", out _));
        Assert.False(vasyaRow.TryGetProperty("phone", out _));
        Assert.False(vasyaRow.TryGetProperty("phoneNumber", out _));
    }

    /// <summary>
    /// Своего права у списка нет, но посторонний его не видит: заголовок чужого
    /// пространства без прав в нём — 403
    /// </summary>
    [Fact]
    public async Task OutsiderCannotSeeMembers()
    {
        var (workspace, _, oleg) = Given.Owner("Рога и Копыта");
        var (strangerWorkspace, strangerToken, sergey) = Given.Owner("Ёжики в Тумане", "Sergey");

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.AddRange(workspace, strangerWorkspace);
            db.Profiles.AddRange(oleg, sergey);
        }, strangerToken, workspace.Id);

        var response = await client.GetAsync("/api/Users/GetProfiles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequiresWorkspaceHeader()
    {
        var (workspace, token, oleg) = Given.Owner();

        var (client, _) = await Given.ApiClient(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(oleg);
        }, token);

        var response = await client.GetAsync("/api/Users/GetProfiles");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
