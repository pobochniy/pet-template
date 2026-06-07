using System.Net.Http.Json;
using PetTemplate.Shared.Dto;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Auth;

public class TelegramAuthApiTests
{
    private const long TestUserId = 12345678;
    private const string TestUsername = "testuser";

    [Fact]
    public async Task CanCreateUser()
    {
        // Arrange
        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: Guid.CreateVersion7().ToString(),
                configureSettings: TestSettings.ConfigureAuth());

        var initData = new TelegramInitDataBuilder()
            .WithUserId(TestUserId)
            .WithUsername(TestUsername)
            .WithBotToken(TestSettings.DefaultTelegramBotToken)
            .Please();
        
        var req = new TelegramAuthDto
        {
            InitData = initData
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/TelegramAuth", req, cancellationToken: CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>(cancellationToken: CancellationToken.None);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token), "Token should not be empty");
        Assert.Equal(TestUsername, result.Profile.UserName);
        Assert.NotNull(result.Permissions); // New user may have no permissions
    }

    [Fact]
    public async Task CanAuthUser()
    {
        // Arrange
        const long existingUserId = 87654321;
        const string existingUsername = "Alexey";
        
        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: Guid.CreateVersion7().ToString(),
                configureSettings: TestSettings.ConfigureAuth(),
                dbArrange: db =>
                {
                    var profile = new PetTemplate.Db.Entity.Profile
                    {
                        Id = Guid.CreateVersion7(),
                        UserName = existingUsername,
                        User = new PetTemplate.Db.Entity.User
                        {
                            TelegramId = existingUserId,
                            UserPermissions = new List<PetTemplate.Db.Entity.UserInPermission>
                            {
                                new()
                                {
                                    PermissionId = PetTemplate.Shared.Enums.PermissionEnum.PermissionManagement
                                }
                            }
                        }
                    };
                    db.Profiles.Add(profile);
                });

        var initData = new TelegramInitDataBuilder()
            .WithUserId(existingUserId)
            .WithUsername("DifferentUsername") // Используем другой username в токене
            .WithBotToken(TestSettings.DefaultTelegramBotToken)
            .Please();
        
        var req = new TelegramAuthDto
        {
            InitData = initData
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/TelegramAuth", req, cancellationToken: CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>(cancellationToken: CancellationToken.None);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token), "Token should not be empty");
        Assert.Equal(existingUsername, result.Profile.UserName); // Username должен быть из БД, а не из токена
        Assert.NotEmpty(result.Permissions); // У пользователя должны быть permissions
        Assert.Contains(PetTemplate.Shared.Enums.PermissionEnum.PermissionManagement, result.Permissions);
    }
}