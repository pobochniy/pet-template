using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Hooks;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Assistant.Tools;
using PetTemplate.Services.Billing;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.Arranges;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Сборка ассистента в приложении (спец. assistant, 15.2 и 17): всё, что живёт в ходе,
/// достаётся из области команды, настройки — из своих секций, стартовый справочник
/// моделей — из конфигурации в пустую таблицу
/// </summary>
public class AssistantWiringTests
{
    /// <summary>Ход, его обработчики и тред собираются из DI приложения</summary>
    [Fact]
    public async Task ScopedAssistantServicesResolve()
    {
        // Arrange
        var services = await Given.Services();
        await using var scope = services.CreateAsyncScope();

        // Act
        var run = scope.ServiceProvider.GetRequiredService<AssistantRunService>();
        var handler = scope.ServiceProvider.GetRequiredService<AssistantRunCommandHandler>();
        var compaction = scope.ServiceProvider.GetRequiredService<AssistantCompactionCommandHandler>();
        var tools = scope.ServiceProvider.GetRequiredService<AssistantTools>();

        // Assert
        Assert.NotNull(run);
        Assert.NotNull(handler);
        Assert.NotNull(compaction);
        // Без разделов проекта в каталоге только инструменты ядра — и без apply_plan
        Assert.Equal(["search_help", "ask_user", "remember_fact"], tools.Catalog().Select(x => x.Name));
        // Трасса и учёт расхода — настоящие (13.1, 13.5), а трасса — та же, что чистит
        // суточная уборка: один контекст с ходом
        Assert.Same(scope.ServiceProvider.GetRequiredService<TraceWriter>(),
            scope.ServiceProvider.GetRequiredService<IRunTrace>());
        Assert.IsType<RunBilling>(scope.ServiceProvider.GetRequiredService<IRunBilling>());
        // Разбор отказов поставщика по умолчанию — OpenAI-совместимый (16.5)
        Assert.Same(OpenAiErrorClassifier.Instance, scope.ServiceProvider.GetRequiredService<IProviderErrorClassifier>());
    }

    /// <summary>
    /// Секции поставщика нет — клиент всё равно строится: ход кончится отказом
    /// поставщика, а не падением старта или выбора модели
    /// </summary>
    [Fact]
    public async Task ChatClientBuildsWithoutConfiguredProvider()
    {
        // Arrange
        var services = await Given.Services();
        var clients = services.GetRequiredService<IModelChatClients>();
        var model = AssistantWorld.AiModel("Без поставщика", 1, DateTime.UtcNow);
        model.Provider = "nobody";

        // Act
        var client = clients.For(model);

        // Assert
        Assert.NotNull(client.GetService<IChatClient>());
    }

    /// <summary>Настройки — из секций <c>AssistantOptions</c> и <c>ModelBreakerOptions</c></summary>
    [Fact]
    public async Task OptionsComeFromSections()
    {
        // Arrange
        var services = await Given.Services();

        // Act
        var assistant = services.GetRequiredService<IOptions<AssistantOptions>>().Value;
        var breaker = services.GetRequiredService<IOptions<ModelBreakerOptions>>().Value;

        // Assert
        Assert.Equal(30, assistant.StepsHard);
        Assert.Equal(TimeSpan.FromHours(2), assistant.StaleAfter);
        Assert.True(assistant.Providers.ContainsKey("MAIN"));
        Assert.Equal(3, breaker.FailuresToOpen);
    }

    /// <summary>
    /// Пустой справочник получает стартовую строку из конфигурации — так ассистенту
    /// есть куда идти до экрана «Модели» в админке (16.2)
    /// </summary>
    [Fact]
    public async Task SeederFillsEmptyCatalog()
    {
        // Act
        var (_, db) = await Given.ApiClient(settings: new Dictionary<string, string?>
        {
            ["AssistantOptions:SeedModels:0:Key"] = "model-from-env"
        });

        // Assert
        var model = await db.AiModels.SingleAsync(CancellationToken.None);
        Assert.Equal("model-from-env", model.Key);
        Assert.Equal("main", model.Provider);
        Assert.True(model.IsEnabled);
        Assert.Null(model.UpdatedByUserId);
    }

    /// <summary>
    /// Справочник не пуст — конфигурация не читается: моделями владеет админка,
    /// и переменная окружения её решений не перепишет
    /// </summary>
    [Fact]
    public async Task SeederLeavesFilledCatalog()
    {
        // Arrange
        var existing = AssistantWorld.AiModel("Из админки", 1, DateTime.UtcNow);

        // Act
        var (_, db) = await Given.ApiClient(x => x.AiModels.Add(existing), settings: new Dictionary<string, string?>
        {
            ["AssistantOptions:SeedModels:0:Key"] = "model-from-env"
        });

        // Assert
        var model = await db.AiModels.SingleAsync(CancellationToken.None);
        Assert.Equal(existing.Id, model.Id);
    }

    /// <summary>Ключа модели в конфигурации нет — строки нет: модель «ни у кого» только отказывала бы</summary>
    [Fact]
    public async Task SeederSkipsEmptyKey()
    {
        // Act
        var (_, db) = await Given.ApiClient();

        // Assert
        Assert.False(await db.AiModels.AnyAsync(CancellationToken.None));
    }

    /// <summary>Два инструмента с одним именем модель не различит — это ошибка сборки</summary>
    [Fact]
    public async Task DuplicateToolNameFails()
    {
        // Arrange
        var world = await AssistantWorld.Build("Тапки и Носки");
        var tools = new AssistantTools(world.Help, world.Context, world.Collector, world.Executor, world.Operations,
            world.Outcome, world.Memory, new AssistantPermissions(world.Db), [new DuplicateHelp()],
            Options.Create(world.Settings));

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => tools.Catalog());

        // Assert
        Assert.Contains("search_help", error.Message);
    }

    /// <summary>Раздел, который по ошибке назвал свой инструмент как инструмент ядра</summary>
    private sealed class DuplicateHelp : IAssistantToolSource
    {
        public string Instructions => string.Empty;

        public IEnumerable<AssistantTool> Tools() => [new(AIFunctionFactory.Create(() => "дубль", "search_help"), [])];
    }
}
