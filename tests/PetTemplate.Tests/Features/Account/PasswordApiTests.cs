using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Errors;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Account;

/// <summary>
/// Задать и сменить пароль — <c>Account/SetPassword</c>, <c>Account/ChangePassword</c>
/// (спец. accounts, 8.1)
/// </summary>
public class PasswordApiTests
{
    private const string NewPassword = "Novyi-parol-42";
    private const string AnyAuthorizedEndpoint = "/api/Account/Logins";
    private static readonly string CurrentPassword = UserBuilder.SuperAdminPassword;

    private static ChangePasswordDto Change(string current = "") => new()
    {
        CurrentPassword = current.Length > 0 ? current : CurrentPassword,
        NewPassword = NewPassword,
        NewPasswordConfirm = NewPassword
    };

    private static SetPasswordDto Set(string password = NewPassword, string? confirm = null) => new()
    {
        NewPassword = password,
        NewPasswordConfirm = confirm ?? password
    };

    /// <summary>Пришедший без пароля задаёт его — и входит по логину и паролю (раздел 16)</summary>
    [Fact]
    public async Task PasswordlessAccountLogsInAfterSet()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutPassword().Please();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow, x => x.Profiles.Add(oleg));
        await (await client.PostAsJsonAsync("/api/Account/SetPassword", Set())).ShouldBeSuccessful();

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto { Login = "Oleg", Password = NewPassword });

        await response.ShouldBeSuccessful();
    }

    [Fact]
    public async Task SetAnswersWithNewSession()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutPassword().Please();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow, x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Account/SetPassword", Set());

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.False(string.IsNullOrWhiteSpace(result!.Token));
        Assert.True(result.Profile.HasPassword);
    }

    /// <summary>Пароль уже есть — «задать» отказывает: для этого есть «сменить»</summary>
    [Fact]
    public async Task CannotSetWhenPasswordExists()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/SetPassword", Set());

        await response.ShouldBeBadRequestWith(AuthMessages.PasswordAlreadySet);
    }

    [Fact]
    public async Task SetRequiresMatchingConfirmation()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutPassword().Please();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow, x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Account/SetPassword", Set(confirm: "Drugoi-parol-42"));

        await response.ShouldBeValidationWith("newPasswordConfirm", ValidationCodes.Mismatch);
    }

    /// <summary>Новый пароль — от 8 символов (8.1)</summary>
    [Fact]
    public async Task SetRejectsShortPassword()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithoutPassword().Please();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow, x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Account/SetPassword", Set("Korotki"));

        await response.ShouldBeValidationWith("newPassword", ValidationCodes.StringLength);
    }

    [Fact]
    public async Task CanChangePassword()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);
        await (await client.PostAsJsonAsync("/api/Account/ChangePassword", Change())).ShouldBeSuccessful();

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto { Login = "Oleg", Password = NewPassword });

        await response.ShouldBeSuccessful();
    }

    /// <summary>Неверный текущий — 422 по полю текущего пароля</summary>
    [Fact]
    public async Task RejectsWrongCurrentPassword()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/ChangePassword", Change("Ne-tot-parol"));

        await response.ShouldBeValidationWith("currentPassword", AuthMessages.WrongPassword.Code);
    }

    /// <summary>
    /// Неверный текущий пароль считает лимит входа (8.1): с украденной сессией иначе подбирали бы
    /// пароль без счёта. Сверх лимита не проходит и верный
    /// </summary>
    [Fact]
    public async Task ChangeIsRateLimited()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);
        for (var i = 0; i < AdminLoginLimiter.ProductAttemptsPerUser; i++)
        {
            await client.PostAsJsonAsync("/api/Account/ChangePassword", Change($"Ne-tot-parol-{i}"));
        }

        var response = await client.PostAsJsonAsync("/api/Account/ChangePassword", Change());

        await response.ShouldBeBadRequestWith(AuthMessages.TooManyAttempts);
    }

    /// <summary>
    /// Смена завершает остальные сессии (8.1): старый токен — 401, хотя только что работал
    /// и его отметка лежала в кэше — запись отзыва кэш сбрасывает
    /// </summary>
    [Fact]
    public async Task ChangeRevokesOldToken()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow.AddMinutes(-5), x => x.Profiles.Add(oleg));
        await (await client.GetAsync(AnyAuthorizedEndpoint)).ShouldBeSuccessful();
        await (await client.PostAsJsonAsync("/api/Account/ChangePassword", Change())).ShouldBeSuccessful();

        var response = await client.GetAsync(AnyAuthorizedEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Текущая сессия продолжается токеном из ответа</summary>
    [Fact]
    public async Task NewTokenFromChangeWorks()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await TestSession.IssuedAt(oleg.Id, DateTime.UtcNow.AddMinutes(-5), x => x.Profiles.Add(oleg));
        var changed = await client.PostAsJsonAsync("/api/Account/ChangePassword", Change());
        var result = await changed.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);

        var response = await client.GetAsync(AnyAuthorizedEndpoint);

        await response.ShouldBeSuccessful();
    }
}
