using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Auth;

/// <summary>
/// Заведение аккаунта входом Telegram и VK там, где держат уникальные индексы
/// (спец. accounts, 3 и 4.2): логин после отвязки входа занят прежним аккаунтом,
/// а одновременные первые входы одним Telegram (VK) упираются в индекс площадки.
/// В ASU — отдельный класс на MySQL (InMemory индексы не держит); здесь все тесты и так
/// на настоящем PostgreSQL, класс оставлен ради темы
/// </summary>
public class ExternalRegistrationTests
{
    private const long MashaTelegramId = 555000999;

    private readonly FakeVkId _vk = new();

    private Task<(HttpClient client, ApplicationContext db)> Client(Action<ApplicationContext>? arrange = null) =>
        Given.ApiClient(arrange, settings: _vk.Settings(), configureServices: _vk.Services());

    private static ExternalCodeDto VkCode() =>
        new() { Code = FakeVkId.ValidCode, Verifier = "pkce-verifier", DeviceId = "device-1", State = "st-1" };

    private static ExternalCodeDto TelegramCode(string? userName = null) => new()
    {
        Code = new TelegramLoginBuilder().WithUserId(MashaTelegramId).WithUsername(userName).Build(),
        State = TelegramLoginBuilder.DefaultNonce
    };

    /// <summary>Олег когда-то пришёл из VK и отвязал его: логин <c>vk&lt;id&gt;</c> остался у него</summary>
    private PetTemplate.Db.Entity.Profile OlegWhoUnlinkedVk() =>
        new UserBuilder($"vk{_vk.VkUserId}").WithContacts("oleg@example.com", "+79990000001").Please().profile;

    [Fact]
    public async Task UnlinkedVkSignsUpAgainWithFreeLogin()
    {
        var oleg = OlegWhoUnlinkedVk();
        var (client, db) = await Client(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/VkId", VkCode());

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.True(result!.IsNewAccount);
        Assert.NotEqual(oleg.Id, result.Profile.Id);
        Assert.Equal($"vk{_vk.VkUserId}_2", result.Profile.UserName);
        Assert.Equal(2, await db.Profiles.CountAsync());
    }

    /// <summary>
    /// Хвост ищется дальше занятых: <c>_2</c> тоже занят — <c>_3</c>. Занятость — без учёта
    /// регистра (<c>citext</c>, 4.1): <c>VK…_2</c> занимает <c>vk…_2</c>
    /// </summary>
    [Fact]
    public async Task FallbackSkipsTakenSuffixes()
    {
        var oleg = OlegWhoUnlinkedVk();
        var vasya = new UserBuilder($"VK{_vk.VkUserId}_2").WithContacts("vasya@example.com", "+79990000002").Please().profile;
        var (client, _) = await Client(x => x.Profiles.AddRange(oleg, vasya));

        var response = await client.PostAsJsonAsync("/api/Auth/VkId", VkCode());

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal($"vk{_vk.VkUserId}_3", result!.Profile.UserName);
    }

    /// <summary>
    /// <c>_</c> в шаблоне поиска занятых — не «любой символ»: <c>vk…x2</c> не занимает <c>vk…_2</c>
    /// </summary>
    [Fact]
    public async Task FallbackTreatsUnderscoreLiterally()
    {
        var oleg = OlegWhoUnlinkedVk();
        var vasya = new UserBuilder($"vk{_vk.VkUserId}x2").WithContacts("vasya@example.com", "+79990000002").Please().profile;
        var (client, _) = await Client(x => x.Profiles.AddRange(oleg, vasya));

        var response = await client.PostAsJsonAsync("/api/Auth/VkId", VkCode());

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal($"vk{_vk.VkUserId}_2", result!.Profile.UserName);
    }

    /// <summary>Без <c>@username</c> и с занятым <c>tg&lt;id&gt;</c> — следующий свободный</summary>
    [Fact]
    public async Task UnlinkedTelegramSignsUpAgainWithFreeLogin()
    {
        var oleg = new UserBuilder($"tg{MashaTelegramId}").WithContacts("oleg@example.com", "+79990000001").Please().profile;
        var (client, _) = await Client(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/TelegramLogin", TelegramCode());

        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal($"tg{MashaTelegramId}_2", result!.Profile.UserName);
    }

    /// <summary>
    /// Двойной клик или две вкладки: одновременные первые входы одним VK заводят один
    /// аккаунт, и все входят в него, а не падают 500 на индексе <c>VkId</c> (4.2)
    /// </summary>
    [Fact]
    public async Task SimultaneousFirstVkSignInsShareOneAccount()
    {
        var (client, db) = await Client();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => client.PostAsJsonAsync("/api/Auth/VkId", VkCode())));

        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            await response.ShouldBeSuccessful();
            ids.Add((await response.Content.ReadFromJsonAsync<AuthenticationResultDto>())!.Profile.Id);
        }
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await db.Users.CountAsync(x => x.VkId == _vk.VkUserId));
    }

    [Fact]
    public async Task SimultaneousFirstTelegramSignInsShareOneAccount()
    {
        var (client, db) = await Client();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => client.PostAsJsonAsync("/api/Auth/TelegramLogin", TelegramCode("masha"))));

        foreach (var response in responses) await response.ShouldBeSuccessful();
        Assert.Equal(1, await db.Users.CountAsync(x => x.TelegramId == MashaTelegramId));
    }

    /// <summary>
    /// Один Telegram привязывают одновременно к двум аккаунтам: один привязывает, второму —
    /// просмотр слияния (опоздал на проверку) или отказ «вход занят» (упёрся в индекс),
    /// но не 500 индекса
    /// </summary>
    [Fact]
    public async Task SimultaneousLinkOfOneTelegramLinksOnlyOne()
    {
        var (olegToken, oleg) = new UserBuilder("Oleg").WithoutTelegram().Please();
        var (vasyaToken, vasya) = new UserBuilder("Vasya").WithoutTelegram().Please();
        var (client, db) = await Client(x => x.Profiles.AddRange(oleg, vasya));

        var responses = await Task.WhenAll(Link(client, olegToken), Link(client, vasyaToken));

        Assert.All(responses, x => Assert.True(x.IsSuccessStatusCode || x.StatusCode == HttpStatusCode.BadRequest));
        var linked = 0;
        foreach (var response in responses.Where(x => x.IsSuccessStatusCode))
        {
            if ((await response.Content.ReadFromJsonAsync<LinkResultDto>())!.Logins != null) linked++;
        }
        Assert.Equal(1, linked);
        Assert.Equal(1, await db.Users.CountAsync(x => x.TelegramId == MashaTelegramId));
    }

    private static Task<HttpResponseMessage> Link(HttpClient client, string token) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/Account/Link")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new ExternalProofDto
            {
                Provider = ExternalProvider.Telegram,
                Code = new TelegramLoginBuilder().WithUserId(MashaTelegramId).Build(),
                State = TelegramLoginBuilder.DefaultNonce
            })
        });
}
