using System.Net;
using System.Net.Http.Json;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Auth;

/// <summary>Вход и регистрация через VK ID на сайте — <c>Auth/VkId</c> (спец. accounts, 5.3)</summary>
public class VkIdApiTests
{
    private readonly FakeVkId _vk = new();

    private Task<(HttpClient client, ApplicationContext db)> Client(Action<ApplicationContext>? arrange = null) =>
        Given.ApiClient(arrange, settings: _vk.Settings(), configureServices: _vk.Services());

    private static ExternalCodeDto Code(string code = FakeVkId.ValidCode) =>
        new() { Code = code, Verifier = "pkce-verifier", DeviceId = "device-1", State = "st-1" };

    /// <summary>Имени VK не отдаёт — логин сразу <c>vk&lt;id&gt;</c> (4.2)</summary>
    [Fact]
    public async Task UnknownVkCreatesAccountWithVkLogin()
    {
        var (client, _) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/VkId", Code());

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.True(result!.IsNewAccount);
        Assert.Equal($"vk{_vk.VkUserId}", result.Profile.UserName);
    }

    [Fact]
    public async Task KnownVkSignsIntoExistingAccount()
    {
        var (_, oleg) = new UserBuilder("Oleg").WithVk(_vk.VkUserId).Please();
        var (client, _) = await Client(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/VkId", Code());

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal(oleg.Id, result!.Profile.Id);
        Assert.False(result.IsNewAccount);
    }

    /// <summary>VK ждёт при обмене то, что отдал с кодом: устройство, verifier и state</summary>
    [Fact]
    public async Task SendsDeviceAndVerifierToVk()
    {
        var (client, _) = await Client();

        await client.PostAsJsonAsync("/api/Auth/VkId", Code());

        Assert.Equal("device-1", _vk.LastRequest!["device_id"]);
        Assert.Equal("pkce-verifier", _vk.LastRequest["code_verifier"]);
        Assert.Equal("st-1", _vk.LastRequest["state"]);
        Assert.Equal("https://example.com/auth/vk", _vk.LastRequest["redirect_uri"]);
    }

    [Fact]
    public async Task RejectsCodeVkDidNotAccept()
    {
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/VkId", Code("stale"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(db.Profiles);
    }

    /// <summary>VK недоступен — отказ входа, а не ошибка сервера (5.4)</summary>
    [Fact]
    public async Task UnreachableVkIsLoginFailureNotServerError()
    {
        _vk.Unreachable = true;
        var (client, db) = await Client();

        var response = await client.PostAsJsonAsync("/api/Auth/VkId", Code());

        await response.ShouldBeBadRequestWith(AuthMessages.ExternalFailed);
        Assert.Empty(db.Profiles);
    }

    /// <summary>Восстановление доступа (8.3): неизвестный VK аккаунт не заводит</summary>
    [Fact]
    public async Task RecoveryWithUnknownVkCreatesNoAccount()
    {
        var (client, db) = await Client();
        var code = Code();
        code.ExistingOnly = true;

        var response = await client.PostAsJsonAsync("/api/Auth/VkId", code);

        await response.ShouldBeNotFoundWith(AuthMessages.LoginNotLinked);
        Assert.Empty(db.Profiles);
    }

    /// <summary>Id приложения не задан — кнопки VK нет (5.1)</summary>
    [Fact]
    public async Task ConfigHasNoVkWhenAppIdIsEmpty()
    {
        var (client, _) = await Given.ApiClient();

        var config = await client.GetFromJsonAsync<ExternalLoginsConfigDto>("/api/Auth/ExternalLogins");

        Assert.Equal(string.Empty, config!.VkAppId);
    }
}
