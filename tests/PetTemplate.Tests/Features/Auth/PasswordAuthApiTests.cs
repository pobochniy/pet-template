using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Errors;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Api.Extensions.Auth;

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
            UserName = "Boris",
            Password = DefaultPassword,
            PasswordConfirm = DefaultPassword
        };

        var response = await client.PostAsJsonAsync("/api/Auth/Register", dto);

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("Boris", result.Profile.UserName);
        // Без названия пространства — регистрация без него: «Меня пригласят — пропустить»
        Assert.Empty(result.PermissionsByWorkspace);
        Assert.Null(result.Profile.PrimaryWorkspaceId);
    }

    [Fact]
    public async Task CanLogin()
    {
        const string username = "Igor";
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
        const string username = "Sergey";
        var userId = Guid.CreateVersion7();
        var dbName = Guid.CreateVersion7().ToString();

        var workspace = new WorkspaceBuilder().Please();
        var (_, profile) = new UserBuilder(username, userId)
            .WithPermissions(workspace.Id, PermissionEnum.PermissionManagement)
            .Please();

        var (client, _) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbName: dbName,
                configureSettings: TestSettings.ConfigureAuth(),
                dbArrange: db =>
                {
                    db.Workspaces.Add(workspace);
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
        Assert.Equal([PermissionEnum.PermissionManagement], result.PermissionsByWorkspace[workspace.Id]);
        // Указателя в профиле нет — основным едет первое доступное (спец. authorization, 7.1)
        Assert.Equal(workspace.Id, result.Profile.PrimaryWorkspaceId);
    }

    [Fact]
    public async Task RejectsWrongPassword()
    {
        const string username = "Maxim";
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
        const string username = "Petya";
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

        await response.ShouldBeBadRequestWith(AuthMessages.UserNameTaken);
    }

    /// <summary>Логин уникален без учёта регистра (спец. accounts, 4.1): колонка `citext`</summary>
    [Fact]
    public async Task CannotRegisterWithTakenUserNameInOtherCase()
    {
        var (_, vasya) = new UserBuilder("Vasya").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(vasya));

        var response = await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = "VASYA", Password = "Parol-Vasi-42", PasswordConfirm = "Parol-Vasi-42"
        });

        await response.ShouldBeBadRequestWith(AuthMessages.UserNameTaken);
    }

    /// <summary>Почта уникальна без учёта регистра (8.4): занятая — отказ с кодом</summary>
    [Fact]
    public async Task CannotRegisterWithTakenEmailInOtherCase()
    {
        var (_, vasya) = new UserBuilder("Vasya").WithContacts("vasya@example.com", "+79990000002").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(vasya));

        var response = await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = "Petya", Email = "VASYA@example.com", Password = "Parol-Peti-42", PasswordConfirm = "Parol-Peti-42"
        });

        await response.ShouldBeBadRequestWith(AuthMessages.EmailTaken);
    }

    /// <summary><c>tg123</c> и <c>vk123</c> — под автоматические логины (4.1)</summary>
    [Fact]
    public async Task CannotRegisterWithReservedUserName()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = "tg123456", Password = "Parol-Vasi-42", PasswordConfirm = "Parol-Vasi-42"
        });

        await response.ShouldBeValidationWith("userName", ValidationCodes.UserNameReserved);
    }

    /// <summary>
    /// Длина логина и почты держится проверкой DTO: у <c>citext</c> в базе предела нет
    /// (спец. infrastructure, 3.1)
    /// </summary>
    [Fact]
    public async Task CannotRegisterWithTooLongUserNameOrEmail()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = new string('v', 21),
            Email = new string('v', 256) + "@example.com",
            Password = "Parol-Vasi-42",
            PasswordConfirm = "Parol-Vasi-42"
        });

        await response.ShouldBeValidationWith("userName", ValidationCodes.UserNameFormat);
        await response.ShouldBeValidationWith("email", ValidationCodes.StringLength);
    }

    /// <summary>Новый пароль — от 8 символов (спец. accounts, 8.1)</summary>
    [Fact]
    public async Task CannotRegisterWithShortPassword()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = "Vasya", Password = "Korotki", PasswordConfirm = "Korotki"
        });

        await response.ShouldBeValidationWith("password", ValidationCodes.StringLength);
    }

    /// <summary>Регистрация отмечает, когда заведён аккаунт (спец. accounts, 11)</summary>
    [Fact]
    public async Task RegistrationStampsCreationTime()
    {
        var (client, db) = await Given.ApiClient();

        await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = "Vlad", Password = "Parol-Vlada-42", PasswordConfirm = "Parol-Vlada-42"
        });

        var user = await db.Users.AsNoTracking().SingleAsync();
        Assert.InRange(user.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
    }

    /// <summary>Логин при входе сравнивается без учёта регистра — как и его занятость</summary>
    [Fact]
    public async Task CanLoginWithUserNameInOtherCase()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn",
            new LoginDto { Login = "OLEG", Password = UserBuilder.SuperAdminPassword });

        await response.ShouldBeSuccessful();
    }

    /// <summary>
    /// Прежний предел пароля на входе (20) снят: пароль, заданный по новым правилам
    /// (до 100), входит
    /// </summary>
    [Fact]
    public async Task CanLoginWithLongPassword()
    {
        const string longPassword = "Ochen-dlinnyi-parol-Vasi-42";
        var (client, _) = await Given.ApiClient();
        await client.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
        {
            UserName = "Vasya", Password = longPassword, PasswordConfirm = longPassword
        });

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto { Login = "Vasya", Password = longPassword });

        await response.ShouldBeSuccessful();
    }

    /// <summary>
    /// Неудачный вход — одной фразой на всю форму (<see cref="AuthMessages.InvalidCredentials"/>):
    /// «нет такого» и «не тот пароль» неразличимы
    /// </summary>
    [Fact]
    public async Task UnknownLoginAnswersLikeWrongPassword()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn",
            new LoginDto { Login = "Nikto", Password = "Parol-Nikogo-42" });

        await response.ShouldBeValidationWith(ValidationFields.Payload, AuthMessages.InvalidCredentials.Code);
    }

    /// <summary>
    /// Подбор пароля через вход продукта заперт (спец. accounts, 8.1): сверх лимита неудач не пускает
    /// и верный пароль. Иначе лимит админки обходился бы этим входом — пароль у админа тот же
    /// </summary>
    [Fact]
    public async Task CannotLoginAfterTooManyFailures()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg));
        for (var i = 0; i < LoginLimiter.ProductAttemptsPerUser; i++)
        {
            await client.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto { Login = "oleg", Password = "wrong-" + i });
        }

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn",
            new LoginDto { Login = "Oleg", Password = UserBuilder.SuperAdminPassword });

        await response.ShouldBeBadRequestWith(AuthMessages.TooManyAttempts);
    }

    /// <summary>Удачный вход место в счёте возвращает: лимит считает только неудачи</summary>
    [Fact]
    public async Task SuccessfulLoginsAreNotCounted()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg));
        var login = new LoginDto { Login = "Oleg", Password = UserBuilder.SuperAdminPassword };
        for (var i = 0; i < LoginLimiter.ProductAttemptsPerUser; i++)
        {
            await (await client.PostAsJsonAsync("/api/Auth/LogIn", login)).ShouldBeSuccessful();
        }

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn", login);

        await response.ShouldBeSuccessful();
    }
}
