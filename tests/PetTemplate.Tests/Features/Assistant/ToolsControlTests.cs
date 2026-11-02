using Microsoft.EntityFrameworkCore;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Help;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Features.Assistant.TestPlan;
using PetTemplate.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Search;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Инструменты ядра (спец. assistant, 5.1, 5.2, 7): <c>apply_plan</c> с согласием
/// и страховкой, <c>ask_user</c>, <c>search_help</c> и каталог
/// </summary>
public sealed class ToolsControlTests : IDisposable
{
    private readonly string _help = Path.Combine(Path.GetTempPath(), "pettemplate-assistant-help-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_help)) Directory.Delete(_help, true);
    }

    /// <summary>
    /// «Да» сразу за показанным планом — применяется, и модель узнаёт исход словами (5.1)
    /// </summary>
    [Fact]
    public async Task ApplyPlanWritesAfterConsent()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        var plan = world.Plan(AssistantWorld.CreateNote("Купить бублики"));
        world.Message(AssistantMessageRole.User, "запиши купить бублики");
        world.Message(AssistantMessageRole.Assistant, "Вот что предлагаю", plan.Id);
        world.Message(AssistantMessageRole.User, "да");
        await world.Save();

        // Act
        var answer = await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        Assert.Contains("применено целиком", answer);
        Assert.Equal(RunOutcomeKind.PlanApplied, world.Outcome.Kind);
        Assert.Equal(plan.Id, world.Outcome.AppliedPlanId);
        Assert.Single(world.Notes.Notes);
    }

    /// <summary>
    /// Реплика через две — это уже разговор о другом, и «да» в ней относится
    /// неизвестно к чему: план не применяется и закрывается (5.1)
    /// </summary>
    [Fact]
    public async Task ConsentAfterOtherTalkIsRefused()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        var plan = world.Plan(AssistantWorld.CreateNote("Купить билеты"));
        world.Message(AssistantMessageRole.Assistant, "Вот что предлагаю", plan.Id);
        world.Message(AssistantMessageRole.User, "а сколько это стоит?");
        world.Message(AssistantMessageRole.Assistant, "Нисколько");
        world.Message(AssistantMessageRole.User, "да");
        await world.Save();

        // Act
        var answer = await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        Assert.Contains("покажи его заново", answer);
        Assert.Empty(world.Notes.Notes);
        var stored = await world.Db.AssistantPlans.AsNoTracking().SingleAsync(x => x.Id == plan.Id, CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Cancelled, stored.Status);
    }

    /// <summary>
    /// Прямое поручение: план собран этим же ходом и применяется сразу, без вопроса
    /// «делаем?». Список человек увидит отчётом (5.1)
    /// </summary>
    [Fact]
    public async Task PlanOfThisTurnAppliesWithoutAsking()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ковши и Черпаки");
        Propose(world, "Починить кран");
        Propose(world, "Купить прокладку");

        // Act
        var answer = await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        Assert.Contains("применено целиком", answer);
        Assert.Equal(2, world.Notes.Notes.Count);

        // План сохранён и помечен применённым: по нему клиент рисует отчёт (12.2.1)
        var plan = await world.Db.AssistantPlans
            .SingleAsync(x => x.Id == world.Outcome.AppliedPlanId, CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Applied, plan.Status);
    }

    /// <summary>
    /// Рамку придумал ассистент — её человек читает до того, как она появится.
    /// Запрет держится кодом, что бы модель об этом ни думала (5.1)
    /// </summary>
    [Fact]
    public async Task PlanWithFrameIsNeverAppliedAtOnce()
    {
        // Arrange
        var world = await AssistantWorld.Build("Скалки и Вёдра");
        world.Collector.Propose(TestNoteKinds.CreateBook, new CreateTestBookArgs { Title = "Рецепты" });
        world.Collector.Propose(TestNoteKinds.CreateNote, new CreateTestNoteArgs { Title = "Шарлотка", BookLink = "#1" });

        // Act
        var answer = await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        Assert.Contains("сразу не применяется", answer);
        Assert.Equal(RunOutcomeKind.None, world.Outcome.Kind);
        Assert.Empty(world.Notes.Books);
        Assert.False(await world.Db.AssistantPlans.AnyAsync(CancellationToken.None));
    }

    /// <summary>Длиннее потолка — список показывается до, а не после (5.1)</summary>
    [Fact]
    public async Task PlanLongerThanTheLimitIsShownFirst()
    {
        // Arrange
        var world = await AssistantWorld.Build("Тёрки и Ступки");
        world.Settings.ImmediateLimit = 2;
        for (var i = 0; i < 3; i++) Propose(world, $"дело {i}");

        // Act
        var answer = await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        Assert.Contains("в плане 3 новых и изменённых дел", answer);
        Assert.Empty(world.Notes.Notes);
    }

    /// <summary>
    /// Применение сразу закрывает показанный раньше план: незакрытый план в треде один (12.2.1)
    /// </summary>
    [Fact]
    public async Task ImmediateApplyClosesThePlanShownBefore()
    {
        // Arrange
        var world = await AssistantWorld.Build("Блины и Оладьи");
        var old = world.Plan(AssistantWorld.CreateNote("Старое дело"));
        await world.Save();
        Propose(world, "Новое дело");

        // Act
        await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        var stored = await world.Db.AssistantPlans.AsNoTracking().SingleAsync(x => x.Id == old.Id, CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Cancelled, stored.Status);
    }

    /// <summary>
    /// Прошло не всё — модель узнаёт, что именно и почему: общего «применено» на такой
    /// исход нет (5.1)
    /// </summary>
    [Fact]
    public async Task PartialFailureIsToldToTheModel()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ватрушки и Пышки");
        Propose(world, "Хлеб");
        Propose(world, TestNoteKinds.Refused);

        // Act
        var answer = await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        Assert.Contains("сделано не всё: прошло 1 из 2", answer);
        Assert.Contains("№2: раздел отказал", answer);
    }

    /// <summary>Не прошло ничего — так и говорится, без «готово» (5.1)</summary>
    [Fact]
    public async Task NothingDoneIsToldPlainly()
    {
        // Arrange
        var world = await AssistantWorld.Build("Кастрюли и Сковородки");
        Propose(world, TestNoteKinds.Refused);

        // Act
        var answer = await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        Assert.StartsWith("НЕ СДЕЛАНО НИЧЕГО", answer);
    }

    /// <summary>Плана нет — применять нечего, модель собирает заново</summary>
    [Fact]
    public async Task ApplyWithoutPlanRefuses()
    {
        // Arrange
        var world = await AssistantWorld.Build("Вилки и Ножи");

        // Act
        var answer = await world.Tools.ApplyPlan(CancellationToken.None);

        // Assert
        Assert.Contains("показанного плана нет", answer);
        Assert.Equal(RunOutcomeKind.None, world.Outcome.Kind);
    }

    /// <summary>
    /// Вопрос заканчивает ход, варианты — подсказки, а не кнопки действия (5.2)
    /// </summary>
    [Fact]
    public async Task AskUserEndsRunWithQuestion()
    {
        // Arrange
        var world = await AssistantWorld.Build("Тапки и Носки");

        // Act
        var answer = world.Tools.AskUser("Какую книгу?", " Рецепты | Ремонт ");

        // Assert
        Assert.Contains("заканчивай ход", answer);
        Assert.Equal(RunOutcomeKind.AskUser, world.Outcome.Kind);
        Assert.Equal(["Рецепты", "Ремонт"], world.Outcome.Options);
    }

    /// <summary>Без операций плана в сборке <c>apply_plan</c> модели не показывается вовсе</summary>
    [Fact]
    public async Task ApplyPlanIsHiddenWithoutOperations()
    {
        // Arrange
        var world = await AssistantWorld.Build("Чашки и Блюдца");
        var operations = new PlanOperations([]);
        var collector = new PlanCollector(operations);
        var tools = new PetTemplate.Services.Assistant.Tools.AssistantTools(world.Help, world.Context, collector,
            new PlanExecutor(world.Db, operations, new AssistantPermissions(world.Db), world.Context),
            operations, world.Outcome, world.Memory, [], Options.Create(world.Settings));

        // Act
        var names = tools.Catalog().Select(x => x.Name).ToArray();

        // Assert
        Assert.Equal(["search_help", "ask_user", "remember_fact"], names);
    }

    /// <summary>Каталог: ядро и инструменты разделов, с <c>apply_plan</c>, когда есть операции</summary>
    [Fact]
    public async Task CatalogHasCoreAndSectionTools()
    {
        // Arrange
        var world = await AssistantWorld.Build("Гвозди и Шурупы");

        // Act
        var names = world.Tools.Catalog().Select(x => x.Name).ToArray();

        // Assert
        Assert.Equal(["search_help", "ask_user", "remember_fact", "apply_plan", "find_test_notes", "create_test_note",
            "create_test_book", "rename_test_note"], names);
    }

    /// <summary>Справки нет (выпуска не было) — модель так и говорит человеку (спец. help, 6.4)</summary>
    [Fact]
    public async Task SearchHelpSaysUnavailable()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пряники и Вафли");

        // Act
        var answer = await world.Tools.SearchHelp("как пригласить коллегу", CancellationToken.None);

        // Assert
        Assert.Contains("справка недоступна", answer);
    }

    /// <summary>
    /// Нашлось — разделы со страницей приложения и версией справки: отвечать
    /// только по найденному (спец. help, 6; assistant, 7)
    /// </summary>
    [Fact]
    public async Task SearchHelpAnswersWithSections()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёлки и Палки");
        world.UseHelp(await PublishedHelp());

        // Act
        var answer = await world.Tools.SearchHelp("позвать коллегу", CancellationToken.None);

        // Assert
        Assert.Contains("\"helpVersion\":\"a1\"", answer);
        Assert.Contains("Как пригласить участника", answer);
        Assert.Contains("/workspace/members", answer);
    }

    /// <summary>Не нашлось — «в справке этого нет», а не догадка</summary>
    [Fact]
    public async Task SearchHelpSaysNothingFound()
    {
        // Arrange
        var world = await AssistantWorld.Build("Сушки и Баранки");
        world.UseHelp(await PublishedHelp());

        // Act
        var answer = await world.Tools.SearchHelp("сколько стоит доставка пиццы", CancellationToken.None);

        // Assert
        Assert.Equal("в справке об этом ничего нет", answer);
    }

    private static void Propose(AssistantWorld world, string title) =>
        world.Collector.Propose(TestNoteKinds.CreateNote, new CreateTestNoteArgs { Title = title });

    /// <summary>
    /// Выпуск справки из одной статьи — каталог с манифестом, как его кладёт публикация
    /// (как в <c>HelpIndexTests</c>); ищет ключевая половина, эмбеддера нет
    /// </summary>
    private async Task<HelpSearch> PublishedHelp()
    {
        Directory.CreateDirectory(_help);
        await File.WriteAllTextAsync(Path.Combine(_help, "workspace.md"), """
            ---
            title: Пространство
            page: /workspace/members
            ---

            ## Как пригласить участника

            На странице участников нажмите «Добавить участника» и отправьте ссылку.

            Как спрашивают: позвать коллегу; добавить человека
            """);
        await File.WriteAllTextAsync(Path.Combine(_help, HelpManifest.FileName),
            """{"commit":"a1","publishedAt":"2026-11-01T10:00:00Z","articles":[],"hash":"x"}""");

        var help = new HelpSearch(
            new ServiceCollection()
                .AddSingleton<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>,
                    NullEmbedder>()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new HelpOptions { Path = _help }),
            Options.Create(new SearchOptions()),
            NullLogger<HelpSearch>.Instance)
        {
            CacheDirectory = null
        };
        await help.Refresh(CancellationToken.None);

        return help;
    }
}
