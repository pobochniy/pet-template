using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Workspace;

/// <summary>
/// Права — из базы, а не из токена (спец. authorization, 2.3): правка прав и состава
/// действует на живую сессию со следующего запроса. Каждый тест сначала прогревает кэш
/// отметки <c>SessionGuard</c> запросом того, чьи права меняются: без сброса после правки
/// ответил бы кэш со старыми правами. И порядок проверок (1.2): посторонний получает
/// <c>403</c> раньше, чем тело запроса проверено
/// </summary>
public class LivePermissionsTests
{
    [Fact]
    public async Task RemovedMemberLosesAccessAtOnce()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (vasyaToken, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead)
            .Please();
        var (client, _) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);
        await (await client.SendAsync(MembersAs(vasyaToken, workspace.Id))).ShouldBeSuccessful();
        await (await client.PostAsync(
            $"/api/Workspace/RemoveMember?userId={vasya.Id}&workspaceId={workspace.Id}", null)).ShouldBeSuccessful();

        var response = await client.SendAsync(MembersAs(vasyaToken, workspace.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokedPermissionStopsWorkingAtOnce()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (vasyaToken, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead, PermissionEnum.PermissionManagement)
            .Please();
        var (client, _) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);
        await (await client.SendAsync(PermissionsAs(vasyaToken, workspace.Id, oleg.Id))).ShouldBeSuccessful();
        await (await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = vasya.Id,
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.WorkspaceMemberRead]
        })).ShouldBeSuccessful();

        var response = await client.SendAsync(PermissionsAs(vasyaToken, workspace.Id, oleg.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Прежний токен создателя открывает новое пространство сразу: права не в токене,
    /// а отметку сбросило создание
    /// </summary>
    [Fact]
    public async Task CreatorsOldTokenOpensNewWorkspace()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (client, _) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
        }, token, workspace.Id);
        await (await client.SendAsync(MembersAs(token, workspace.Id))).ShouldBeSuccessful();
        var created = await client.PostAsJsonAsync("/api/Workspace/Create", new WorkspaceDto { Name = "Котики и Собачки" });
        var createdId = (await created.Content.ReadFromJsonAsync<WorkspaceCreatedDto>())!.WorkspaceId;

        var response = await client.SendAsync(MembersAs(token, createdId));

        await response.ShouldBeSuccessful();
    }

    /// <summary>
    /// Посторонний с неправильным телом — <c>403</c>, а не <c>422</c>: права проверяются
    /// фильтром авторизации, до разбора тела и <c>[ValidateRequest]</c>
    /// </summary>
    [Fact]
    public async Task NonMemberGetsForbiddenBeforeValidation()
    {
        var (workspace, _, oleg) = Given.Owner();
        var (strangerToken, sergey) = new UserBuilder("Sergey").Please();
        var (client, _) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, sergey);
        }, strangerToken, workspace.Id);

        // Пустой набор прав — 422 у поля для участника с правом (спец. authorization, 5)
        var response = await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = oleg.Id,
            WorkspaceId = workspace.Id,
            Permissions = []
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Чужое пространство в теле — <c>403</c> раньше <c>422</c>: сверка с заголовком
    /// стоит перед <c>[ValidateRequest]</c>
    /// </summary>
    [Fact]
    public async Task ForeignWorkspaceInBodyGetsForbiddenBeforeValidation()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (client, _) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.Add(oleg);
        }, token, workspace.Id);

        var response = await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = oleg.Id,
            WorkspaceId = Guid.NewGuid(),
            Permissions = []
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// «Мои права» (2.3) — свежие из базы, без перевыпуска токена и без заголовка пространства:
    /// клиент перечитывает ими меню после правки прав чужими руками
    /// </summary>
    [Fact]
    public async Task MyPermissionsShowChangeWithoutNewToken()
    {
        var (workspace, token, oleg) = Given.Owner();
        var (vasyaToken, vasya) = new UserBuilder("Vasya")
            .WithPermissions(workspace.Id, PermissionEnum.WorkspaceMemberRead, PermissionEnum.PermissionManagement)
            .Please();
        var (client, _) = await Given.ApiClient(arrange =>
        {
            arrange.Workspaces.Add(workspace);
            arrange.Profiles.AddRange(oleg, vasya);
        }, token, workspace.Id);
        Assert.Equal(
            new[] { PermissionEnum.WorkspaceMemberRead, PermissionEnum.PermissionManagement }.Order(),
            (await MyPermissions(client, vasyaToken))[workspace.Id]);
        await (await client.PostAsJsonAsync("/api/Users/SetPermissions", new UserAndPermissionsDto
        {
            UserId = vasya.Id,
            WorkspaceId = workspace.Id,
            Permissions = [PermissionEnum.WorkspaceMemberRead]
        })).ShouldBeSuccessful();

        var permissions = await MyPermissions(client, vasyaToken);

        Assert.Equal([PermissionEnum.WorkspaceMemberRead], permissions[workspace.Id]);
        Assert.Single(permissions);
    }

    private static async Task<Dictionary<Guid, List<PermissionEnum>>> MyPermissions(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/Workspace/MyPermissions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        await response.ShouldBeSuccessful();
        return (await response.Content.ReadFromJsonAsync<Dictionary<Guid, List<PermissionEnum>>>())!;
    }

    private static HttpRequestMessage MembersAs(string token, Guid workspaceId) =>
        As(token, workspaceId, "/api/Users/GetProfiles");

    private static HttpRequestMessage PermissionsAs(string token, Guid workspaceId, Guid userId) =>
        As(token, workspaceId, $"/api/Users/GetPermissions?userId={userId}&workspaceId={workspaceId}");

    private static HttpRequestMessage As(string token, Guid workspaceId, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(WorkspaceConstants.Header, workspaceId.ToString());
        return request;
    }
}
