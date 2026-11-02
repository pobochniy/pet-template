using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Interfaces;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Auth;

/// <summary>Вход из mini-app VK — <c>Auth/VkAuth</c> по подписанным параметрам запуска (спец. accounts, 5.6)</summary>
public class VkAuthApiTests
{
    private const long OlegVkId = 2308481;
    private const long VasyaVkId = 9876543;

    private static Action<IServiceCollection> Sign(bool valid) =>
        services =>
        {
            var mock = new Mock<IVkService>();
            mock.Setup(x => x.IsSignValid(It.IsAny<string>())).Returns(valid);
            services.AddSingleton(mock.Object);
        };

    private static Task<(HttpClient client, ApplicationContext db)> Client(
        Action<ApplicationContext>? arrange = null, bool validSign = true) =>
        Given.ApiClient(arrange, configureServices: Sign(validSign));

    /// <summary>
    /// Заведение аккаунта входом VK — «Начать» на развилке (5.6): сам <c>VkAuth</c>
    /// неизвестный вход не заводит
    /// </summary>
    private static RegisterExternalDto Register(VkAuthDto launch) => new()
    {
        Proof = new ExternalProofDto { Provider = ExternalProvider.Vk, VkLaunchParams = launch.LaunchParams }
    };

    private static VkAuthDto BuildDto(long vkUserId, string language = "ru", DateTimeOffset? signedAt = null) => new()
    {
        LaunchParams = $"vk_access_token_settings=&vk_app_id=51797414&vk_are_notifications_enabled=0&vk_is_app_user=1" +
                       $"&vk_is_favorite=0&vk_language={language}&vk_platform=desktop_web&vk_ref=other" +
                       $"&vk_ts={(signedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()}&vk_user_id={vkUserId}&sign=test_sign"
    };

    /// <summary>
    /// Имени VK не отдаёт — логин нового аккаунта сразу <c>vk&lt;id&gt;</c> (4.2)
    /// </summary>
    [Fact]
    public async Task CanRegisterViaVk()
    {
        var (client, db) = await Client();
        var before = DateTime.UtcNow;

        var response = await client.PostAsJsonAsync("/api/Auth/RegisterExternal", Register(BuildDto(OlegVkId)));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.True(result!.IsNewAccount);
        Assert.Equal($"vk{OlegVkId}", result.Profile.UserName);
        Assert.True(result.Profile.UserNameIsGenerated);
        Assert.Empty(result.PermissionsByWorkspace);
        Assert.InRange((await db.Users.AsNoTracking().SingleAsync()).CreatedAtUtc, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task CanAuthExistingVkUser()
    {
        var (_, oleg) = new UserBuilder("Олег").WithVk(OlegVkId).Please();
        var (client, _) = await Client(db => db.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth", BuildDto(OlegVkId));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.False(string.IsNullOrWhiteSpace(result!.Token));
        Assert.Equal("Олег", result.Profile.UserName);
    }

    [Fact]
    public async Task ReturnsPermissionsForVkUser()
    {
        var workspace = new WorkspaceBuilder().Please();
        var (_, vasya) = new UserBuilder("Vasya").WithVk(VasyaVkId)
            .WithPermissions(workspace.Id, PermissionEnum.PermissionManagement)
            .Please();
        var (client, _) = await Client(db =>
        {
            db.Workspaces.Add(workspace);
            db.Profiles.Add(vasya);
        });

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth", BuildDto(VasyaVkId));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Contains(PermissionEnum.PermissionManagement, result!.PermissionsByWorkspace[workspace.Id]);
    }

    [Fact]
    public async Task RejectsBadSign()
    {
        var (client, _) = await Client(validSign: false);

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth", BuildDto(OlegVkId));

        await response.ShouldBeBadRequestWith(AuthMessages.ExternalFailed);
    }

    /// <summary>
    /// Язык профиля, заводимого входом через VK, — <c>vk_language</c> из параметров
    /// запуска: своей формы у этого входа нет, и без него профиль доставался бы человеку
    /// с <c>ru</c> независимо от того, что он видит в VK
    /// </summary>
    [Fact]
    public async Task VkLanguageWritesItToProfile()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/RegisterExternal",
            Register(BuildDto(OlegVkId, LanguageConstants.English)));

        await response.ShouldBeSuccessful();
        Assert.Equal(LanguageConstants.English, (await db.Profiles.AsNoTracking().SingleAsync()).Language);
    }

    /// <summary>Язык VK, которого у нас нет, вход не отбивает: человек его не выбирал</summary>
    [Fact]
    public async Task UnsupportedVkLanguageFallsBackToDefault()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/RegisterExternal", Register(BuildDto(OlegVkId, "de")));

        await response.ShouldBeSuccessful();
        Assert.Equal(LanguageConstants.Default, (await db.Profiles.AsNoTracking().SingleAsync()).Language);
    }

    /// <summary>
    /// Подпись старше суток — отказ, хоть она и верная: подсмотренные однажды параметры
    /// запуска не должны быть вечным входом (5.6)
    /// </summary>
    [Fact]
    public async Task RejectsStaleLaunchParams()
    {
        var (client, _) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth",
            BuildDto(OlegVkId, signedAt: DateTimeOffset.UtcNow.AddDays(-2)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary><c>Auth/MiniAuth</c> — копия <c>VkAuth</c> без клиента — удалён (5.6)</summary>
    [Fact]
    public async Task MiniAuthIsGone()
    {
        var (client, _) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/MiniAuth", BuildDto(OlegVkId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Неизвестный VK — развилка, аккаунт не заводится (5.6)</summary>
    [Fact]
    public async Task UnknownVkCreatesNothing()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth", BuildDto(OlegVkId));

        await response.ShouldBeNotFoundWith(AuthMessages.UnknownLogin);
        Assert.Empty(db.Profiles);
    }

    /// <summary>
    /// Mini-app VK открыт по приглашению (<c>vk.com/app&lt;id&gt;#inv_…</c>): развилки нет,
    /// аккаунт заводится сразу, как у Telegram
    /// </summary>
    [Fact]
    public async Task UnknownVkWithAliveInvitationRegistersWithoutFork()
    {
        var (response, _) = await VkAuthWithInvitation(builder => builder);

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal($"vk{OlegVkId}", result!.Profile.UserName);
    }

    /// <summary>
    /// Фрагмент VK не подписан — мёртвое приглашение развилку не пропускает: верим
    /// приглашению, а не параметру
    /// </summary>
    [Fact]
    public async Task UnknownVkWithRevokedInvitationStillAsks()
    {
        var (response, db) = await VkAuthWithInvitation(builder => builder.Revoked());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.Users.AnyAsync(x => x.VkId == OlegVkId));
    }

    private static async Task<(HttpResponseMessage response, ApplicationContext db)> VkAuthWithInvitation(
        Func<WorkspaceInvitationBuilder, WorkspaceInvitationBuilder> arrange)
    {
        var (workspace, _, owner) = Given.Owner();
        var invitation = arrange(new WorkspaceInvitationBuilder(workspace.Id, owner.Id)).Please();

        var (client, db) = await Client(x =>
        {
            x.Workspaces.Add(workspace);
            x.Profiles.Add(owner);
            x.WorkspaceInvitations.Add(invitation);
        });

        var dto = BuildDto(OlegVkId);
        dto.Invitation = invitation.Token;
        return (await client.PostAsJsonAsync("/api/Auth/VkAuth", dto), db);
    }
}
