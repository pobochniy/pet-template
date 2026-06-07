using System.Net;
using System.Net.Http.Json;
using LA.Atheneum.Dto.Auth;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Interfaces;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Asserts;

namespace PetTemplate.Tests.Features.Auth;

public class VkAuthApiTests
{
    private const long OlegVkId = 2308481;
    private const long VasyaVkId = 9876543;

    private static Action<IServiceCollection> ValidSign() =>
        services =>
        {
            var mock = new Mock<IVkService>();
            mock.Setup(x => x.IsSignValid(It.IsAny<string>())).Returns(true);
            services.AddSingleton(mock.Object);
        };

    private static Action<IServiceCollection> InvalidSign() =>
        services =>
        {
            var mock = new Mock<IVkService>();
            mock.Setup(x => x.IsSignValid(It.IsAny<string>())).Returns(false);
            services.AddSingleton(mock.Object);
        };

    private static VkAuthDto BuildDto(long vkUserId) => new()
    {
        vk_user_id = vkUserId,
        sign = "test_sign",
        vk_app_id = 51797414,
        vk_are_notifications_enabled = 0,
        vk_is_app_user = 1,
        vk_is_favorite = 0,
        vk_language = "ru",
        vk_platform = "desktop_web",
        vk_ref = "other",
        vk_ts = 1701528645
    };

    [Fact]
    public async Task CanRegisterViaVk()
    {
        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: Guid.CreateVersion7().ToString(),
                configureSettings: TestSettings.ConfigureAuth(),
                configureServices: ValidSign());

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth", BuildDto(OlegVkId));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.NotNull(result.Profile);
        Assert.NotNull(result.Permissions);
    }

    [Fact]
    public async Task CanAuthExistingVkUser()
    {
        const string existingUsername = "Олег";

        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: Guid.CreateVersion7().ToString(),
                configureSettings: TestSettings.ConfigureAuth(),
                configureServices: ValidSign(),
                dbArrange: db =>
                {
                    db.Profiles.Add(new PetTemplate.Db.Entity.Profile
                    {
                        Id = Guid.CreateVersion7(),
                        UserName = existingUsername,
                        User = new PetTemplate.Db.Entity.User { VkId = OlegVkId }
                    });
                });

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth", BuildDto(OlegVkId));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(existingUsername, result.Profile.UserName);
    }

    [Fact]
    public async Task ReturnsPermissionsForVkUser()
    {
        var userId = Guid.CreateVersion7();

        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: Guid.CreateVersion7().ToString(),
                configureSettings: TestSettings.ConfigureAuth(),
                configureServices: ValidSign(),
                dbArrange: db =>
                {
                    db.Profiles.Add(new PetTemplate.Db.Entity.Profile
                    {
                        Id = userId,
                        UserName = "Васян",
                        User = new PetTemplate.Db.Entity.User
                        {
                            Id = userId,
                            VkId = VasyaVkId,
                            UserPermissions =
                            [
                                new UserInPermission { UserId = userId, PermissionId = PermissionEnum.PermissionManagement }
                            ]
                        }
                    });
                });

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth", BuildDto(VasyaVkId));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Permissions);
        Assert.Contains(PermissionEnum.PermissionManagement, result.Permissions);
    }

    [Fact]
    public async Task RejectsBadSign()
    {
        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: Guid.CreateVersion7().ToString(),
                configureSettings: TestSettings.ConfigureAuth(),
                configureServices: InvalidSign());

        var response = await client.PostAsJsonAsync("/api/Auth/VkAuth", BuildDto(OlegVkId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CanRegisterViaMiniApp()
    {
        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: Guid.CreateVersion7().ToString(),
                configureSettings: TestSettings.ConfigureAuth(),
                configureServices: ValidSign());

        var response = await client.PostAsJsonAsync("/api/Auth/MiniAuth", BuildDto(OlegVkId));

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.NotNull(result.Profile);
    }
}
