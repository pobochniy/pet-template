using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Help;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Dto.Help;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.Asserts;

namespace PetTemplate.Tests.Features.Help;

/// <summary>
/// Справка на сайте (спец. help, 6.5): список статей и статья целиком из выпуска в памяти —
/// анонимно, без сессии и заголовка пространства
/// </summary>
public class HelpApiTests
{
    private static readonly HelpPage Account = new("account", "Аккаунт и вход", "## Как сменить пароль\n\nОткройте профиль.");

    private static readonly HelpPage Workspace = new("workspace", "Пространство", "## Как пригласить\n\nНажмите «Пригласить».");

    [Fact]
    public async Task ListsArticlesAnonymously()
    {
        // Arrange
        var client = await Client(Account, Workspace);

        // Act
        var response = await client.GetAsync("/api/Help");

        // Assert
        await response.ShouldBeSuccessful();
        var list = await response.Content.ReadFromJsonAsync<HelpArticleSummaryDto[]>();
        Assert.NotNull(list);
        Assert.Equal(["account", "workspace"], list.Select(x => x.Slug));
        Assert.Equal("Аккаунт и вход", list[0].Title);
    }

    /// <summary>Контракт клиента — camelCase: <c>slug</c>, <c>title</c>, <c>markdown</c></summary>
    [Fact]
    public async Task ReturnsArticleMarkdown()
    {
        // Arrange
        var client = await Client(Account, Workspace);

        // Act
        var response = await client.GetAsync("/api/Help/account");

        // Assert
        await response.ShouldBeSuccessful();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("account", json.RootElement.GetProperty("slug").GetString());
        Assert.Equal("Аккаунт и вход", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(Account.Markdown, json.RootElement.GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task UnknownArticleIsNotFound()
    {
        // Arrange
        var client = await Client(Account);

        // Act
        var response = await client.GetAsync("/api/Help/balance");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Выпуска справки нет — пустой список, а не ошибка</summary>
    [Fact]
    public async Task NoReleaseMeansEmptyList()
    {
        // Arrange
        var client = await Client();

        // Act
        var list = await client.GetFromJsonAsync<HelpArticleSummaryDto[]>("/api/Help");

        // Assert
        Assert.Empty(list!);
    }

    /// <summary>Приложение без сессии; справка в памяти — подставленным индексом (пустой — выпуска нет)</summary>
    private static async Task<HttpClient> Client(params HelpPage[] pages)
    {
        var help = new HelpSearch(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new HelpOptions { Path = Path.Combine(Path.GetTempPath(), "pettemplate-no-help") }),
            Options.Create(new SearchOptions()),
            NullLogger<HelpSearch>.Instance);
        if (pages.Length > 0) help.Use(new HelpIndex("a1", [], 0, pages));

        var (client, _) = await new ApiApplicationFactory<Program>().SetupApplication(
            dbName: Guid.NewGuid().ToString(),
            configureSettings: TestSettings.ConfigureAuth(),
            configureServices: services => services.Replace(ServiceDescriptor.Singleton(help)));

        return client;
    }
}
