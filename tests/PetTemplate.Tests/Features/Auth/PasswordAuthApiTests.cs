using System.Net;
using System.Net.Http.Json;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Auth;

public class PasswordAuthApiTests
{
    private static readonly string DefaultPassword = UserBuilder.SuperAdminTelegramAuth;

    [Fact]
    public async Task CanRegister()
    {
        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: Guid.CreateVersion7().ToString(),
                configureSettings: TestSettings.ConfigureAuth());

        var dto = new RegisterDto
        {
            UserName = "Борис",
            Password = DefaultPassword,
            PasswordConfirm = DefaultPassword
        };

        var response = await client.PostAsJsonAsync("/api/Auth/Register", dto);

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("Борис", result.Profile.UserName);
        Assert.NotNull(result.Permissions);
    }

    [Fact]
    public async Task CanLogin()
    {
        const string username = "Игорь";
        var dbName = Guid.CreateVersion7().ToString();

        var (registerClient, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: dbName,
                configureSettings: TestSettings.ConfigureAuth());

        await registerClient.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = username,
            Password = DefaultPassword,
            PasswordConfirm = DefaultPassword
        });

        var (loginClient, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: dbName,
                configureSettings: TestSettings.ConfigureAuth());

        var response = await loginClient.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto
        {
            Login = username,
            Password = DefaultPassword
        });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(username, result.Profile.UserName);
    }

    [Fact]
    public async Task CanLoginWithPermissions()
    {
        const string username = "Сергей";
        var userId = Guid.CreateVersion7();
        var dbName = Guid.CreateVersion7().ToString();

        var (oleg, profile) = new UserBuilder(username, userId).Please();

        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: dbName,
                configureSettings: TestSettings.ConfigureAuth(),
                dbArrange: db =>
                {
                    profile.User.UserPermissions =
                    [
                        new PetTemplate.Db.Entity.UserInPermission
                        {
                            UserId = userId,
                            PermissionId = PermissionEnum.PermissionManagement
                        }
                    ];
                    db.Profiles.Add(profile);
                });

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto
        {
            Login = username,
            Password = DefaultPassword
        });

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Permissions);
        Assert.Contains(PermissionEnum.PermissionManagement, result.Permissions);
    }

    [Fact]
    public async Task RejectsWrongPassword()
    {
        const string username = "Максим";
        var dbName = Guid.CreateVersion7().ToString();

        var (registerClient, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: dbName,
                configureSettings: TestSettings.ConfigureAuth());

        await registerClient.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = username,
            Password = DefaultPassword,
            PasswordConfirm = DefaultPassword
        });

        var (loginClient, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: dbName,
                configureSettings: TestSettings.ConfigureAuth());

        var response = await loginClient.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto
        {
            Login = username,
            Password = "WrongPassword1"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectsDuplicateUsername()
    {
        const string username = "Петя";
        var dbName = Guid.CreateVersion7().ToString();

        var (client1, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: dbName,
                configureSettings: TestSettings.ConfigureAuth());

        await client1.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = username,
            Password = DefaultPassword,
            PasswordConfirm = DefaultPassword
        });

        var (client2, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: dbName,
                configureSettings: TestSettings.ConfigureAuth());

        var response = await client2.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = username,
            Password = DefaultPassword,
            PasswordConfirm = DefaultPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
