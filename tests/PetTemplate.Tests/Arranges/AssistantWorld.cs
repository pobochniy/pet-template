using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Assistant.Tools;
using PetTemplate.Services.Assistant.Transcription;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Help;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Search;
using PetTemplate.Tests.Mocks;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Features.Assistant.TestPlan;

namespace PetTemplate.Tests.Arranges;

/// <summary>
/// Пространство с одним человеком, справочником из одной модели и ассистентом поверх
/// контекста теста (спец. assistant). Модель — заранее записанные ответы
/// (<see cref="FakeChatClient"/>), инструменты разделов — тестовые заметки
/// (<c>Features/Assistant/TestPlan</c>): предметных сущностей в ядре нет.
///
/// Сервисы собираются руками, а не из DI приложения: тесту нужны настройки, которые
/// он правит до хода (<see cref="Settings"/>), и общие на весь мир копилка, исход
/// и контекст хода — как у одной области команды в бою
/// </summary>
public sealed class AssistantWorld
{
    private readonly ModelBreakerOptions _breakerSettings = new();

    private int _messages;

    private AssistantThreadService? _threads;

    public Guid WorkspaceId { get; private init; }

    /// <summary>Автор просьбы — от его имени идут ходы и применяются планы</summary>
    public Guid Author { get; private init; }

    public ApplicationContext Db { get; private init; } = null!;

    /// <summary>
    /// HTTP-клиент этого же приложения: им проверяются эндпоинты раздела 14.
    /// Токен — автора мира, заголовок пространства проставлен
    /// </summary>
    public HttpClient Client { get; private init; } = null!;

    /// <summary>Модель справочника, которой ходят по умолчанию</summary>
    public AiModel Model { get; private init; } = null!;

    /// <summary>
    /// «Сейчас» у теста одно на всех: у мира, у контекста хода и у прогона.
    /// Два разных DateTime.UtcNow разъезжаются, и проверки возраста плана
    /// срабатывали бы через раз
    /// </summary>
    public DateTime Now { get; private init; }

    /// <summary>
    /// Настройки ассистента этого теста: предохранители правятся до <see cref="Runner"/>,
    /// чтобы тридцатишаговый потолок не приходилось выкручивать тридцатью ответами
    /// </summary>
    public AssistantOptions Settings { get; } = new();

    /// <summary>Настройки размыкателя — правятся до <see cref="Runner"/>, как и <see cref="Settings"/></summary>
    public ModelBreakerOptions BreakerSettings => _breakerSettings;

    /// <summary>
    /// Учёт расхода и лимит пространства по умолчанию (спец. assistant, 13.5–13.6):
    /// без потолков, пока тест их не задаст до <see cref="Threads"/>
    /// </summary>
    public SpendOptions SpendSettings { get; } = new();

    /// <summary>Метрики ассистента — в бою синглтон приложения; тест метрик ставит свои до <see cref="Runner"/></summary>
    public AssistantMetrics Metrics { get; set; } = new();

    /// <summary>Метрики расхода — в бою синглтон приложения</summary>
    public BillingMetrics BillingMetrics { get; } = new();

    public AssistantContext Context { get; private init; } = null!;

    /// <summary>Копилка операций хода: то, что инструменты задумали, но не сделали</summary>
    public PlanCollector Collector { get; private set; } = null!;

    public PlanOperations Operations { get; private set; } = null!;

    public PlanExecutor Executor { get; private set; } = null!;

    /// <summary>Чем ход кончился по воле инструментов: вопрос или применение</summary>
    public RunOutcome Outcome { get; private init; } = null!;

    /// <summary>Записи тестового раздела — то, что пишут операции плана</summary>
    public TestNoteStore Notes { get; } = new();

    /// <summary>Справка мира: пустая — «справка недоступна», пока тест не подставит индекс</summary>
    public HelpSearch Help { get; private set; } = null!;

    /// <summary>Инструменты от имени автора — с тем же контекстом, что у настоящего хода</summary>
    public AssistantTools Tools { get; private set; } = null!;

    /// <summary>Отмена живых прогонов — синглтон приложения, здесь один на мир теста</summary>
    public RunCancellation Cancellation { get; } = new();

    /// <summary>Сигнал долгого опроса — в бою синглтон приложения</summary>
    public RunNotifier Notifier { get; } = new();

    /// <summary>Память о человеке (12.5) поверх контекста теста и настроек мира</summary>
    public MemoryService Memory => new(Db, Options.Create(Settings));

    /// <summary>Тред: отправка сообщений, очередь ходов, «Остановить»</summary>
    public AssistantThreadService Threads => _threads ??= ThreadsOver(Db);

    /// <summary>Тред поверх своего контекста — как у второй вкладки (<see cref="NewContext"/>)</summary>
    public AssistantThreadService ThreadsOver(ApplicationContext db) => new(
        db, Cancellation, new WorkspaceLimits(db, Options.Create(SpendSettings)),
        new AiModelCatalog(db, new ModelBreaker(db, Options.Create(_breakerSettings), NullLogger<ModelBreaker>.Instance, Metrics),
            NullLogger<AiModelCatalog>.Instance, Metrics),
        new MemoryService(db, Options.Create(Settings)),
        BillingMetrics, Options.Create(Settings));

    /// <summary>Размыкатель поверх контекста теста: состояние — в строках справочника</summary>
    public ModelBreaker Breaker => new(Db, Options.Create(_breakerSettings), NullLogger<ModelBreaker>.Instance, Metrics);

    /// <summary>Справочник моделей: в базе теста уже лежит <see cref="Model"/></summary>
    public AiModelCatalog Models => new(Db, Breaker, NullLogger<AiModelCatalog>.Instance, Metrics);

    /// <param name="workspaceName">Название пространства — видно в промпте</param>
    /// <param name="permissions">
    /// Без списка человек всесилен — так выглядит большинство тестов; со списком
    /// проверяется, чего ассистент не сделает (раздел 9)
    /// </param>
    public static async Task<AssistantWorld> Build(string workspaceName, params PermissionEnum[] permissions)
    {
        var now = DateTime.UtcNow;
        var workspace = new WorkspaceBuilder(workspaceName).Please();
        var builder = new UserBuilder("Oleg").WithPrimaryWorkspace(workspace.Id);
        builder = permissions.Length == 0
            ? builder.WithAllPermissions(workspace.Id)
            : builder.WithPermissions(workspace.Id, permissions);
        var (token, author) = builder.Please();
        var model = AiModel("Основная", 1, now);

        var (client, db) = await Given.ApiClient(x =>
        {
            x.Workspaces.Add(workspace);
            x.Profiles.Add(author);
            x.AiModels.Add(model);
        }, token, workspace.Id);

        var context = new AssistantContext
        {
            UserId = author.Id,
            WorkspaceId = workspace.Id,
            NowUtc = now,
            Today = DateOnly.FromDateTime(now)
        };

        var world = new AssistantWorld
        {
            WorkspaceId = workspace.Id,
            Author = author.Id,
            Now = now,
            Db = db,
            Client = client,
            Model = model,
            Context = context,
            Outcome = new RunOutcome(),
            Help = EmptyHelp()
        };

        return world.Assemble();
    }

    /// <summary>Строка справочника моделей — включённая и замкнутая</summary>
    public static AiModel AiModel(string name, int priority, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Provider = "main",
        Key = "model-" + name,
        DisplayName = name,
        Priority = priority,
        IsEnabled = true,
        UpdatedUtc = now
    };

    /// <summary>Справка мира с подставленным индексом — для <c>search_help</c></summary>
    public void UseHelp(HelpSearch help)
    {
        Help = help;
        Assemble();
    }

    /// <summary>Ход целиком поверх заранее записанной модели</summary>
    public AssistantRunService Runner(FakeChatClient chat) => Runner(new FakeModelChatClients(chat));

    /// <summary>Ход, у которого видно, под какую строку справочника он брал клиента</summary>
    public AssistantRunService Runner(IModelChatClients clients)
    {
        var settings = Options.Create(Settings);

        return new AssistantRunService(
            Db,
            clients,
            Models,
            Breaker,
            Tools,
            Context,
            Collector,
            Executor,
            Outcome,
            new LoopGuards(settings),
            new SystemPromptBuilder(settings, NullLogger<SystemPromptBuilder>.Instance),
            new HistoryBuilder(Db, settings),
            Memory,
            new TraceWriter(Db, settings),
            new RunBilling(Db, BillingMetrics, NullLogger<RunBilling>.Instance),
            Notifier,
            Metrics,
            settings);
    }

    /// <summary>Голос поверх движка теста: тред, лимит и учёт — те же, что у мира</summary>
    public VoiceInputService Voice(ITranscriptionService engine) =>
        new(Db, Threads, engine, BillingMetrics, Options.Create(Settings));

    /// <summary>Обработчик команды хода — то, что в бою дёргает воркер очереди</summary>
    public AssistantRunCommandHandler Handler(FakeChatClient chat) => new(
        Db,
        Runner(chat),
        Threads,
        Cancellation,
        new NoLease(),
        Options.Create(Settings),
        NullLogger<AssistantRunCommandHandler>.Instance);

    /// <summary>Обработчик компакции поверх записанной модели</summary>
    public AssistantCompactionCommandHandler Compaction(FakeChatClient chat) => new(
        Db,
        new FakeModelChatClients(chat),
        Models,
        Breaker,
        BillingMetrics,
        Options.Create(Settings),
        NullLogger<AssistantCompactionCommandHandler>.Instance);

    /// <summary>Прогон в работе — то, что ставит в очередь <c>POST Send</c></summary>
    public AssistantRun StartRun(string timeZone = "UTC", string language = "ru")
    {
        var run = new AssistantRun
        {
            Id = Guid.CreateVersion7(Now),
            UserId = Author,
            WorkspaceId = WorkspaceId,
            TimeZone = timeZone,
            Language = language,
            Status = AssistantRunStatus.Running,
            StartedAtUtc = Now
        };
        Db.AssistantRuns.Add(run);

        return run;
    }

    /// <summary>Показанный человеку план, ждущий «да»</summary>
    public AssistantPlan Plan(params PlanOperation[] operations) => Plan(AssistantPlanStatus.Pending, operations);

    public AssistantPlan Plan(AssistantPlanStatus status, params PlanOperation[] operations)
    {
        var plan = new AssistantPlan
        {
            Id = Guid.CreateVersion7(Now),
            UserId = Author,
            WorkspaceId = WorkspaceId,
            Status = status,
            Operations = PlanJson.Serialize(new PlanDocument { Operations = [.. operations] }),
            CreatedAtUtc = Now
        };
        Db.AssistantPlans.Add(plan);

        return plan;
    }

    /// <summary>
    /// Сообщение в ленте треда. Идентификатор — <c>Guid</c> версии 7 от заданного
    /// момента: порядок ленты держится на нём (12.2), и в тесте он обязан быть
    /// таким же определённым, как в жизни. Каждое следующее — минутой позже,
    /// но раньше «сейчас» мира: лента лежит до хода, который тест запускает
    /// </summary>
    public AssistantMessage Message(AssistantMessageRole role, string content, Guid? planId = null,
        string? options = null)
    {
        var at = Now.AddHours(-1).AddMinutes(_messages++);
        var message = new AssistantMessage
        {
            Id = Guid.CreateVersion7(at),
            UserId = Author,
            WorkspaceId = WorkspaceId,
            Role = role,
            Content = content,
            PlanId = planId,
            Options = options,
            CreatedAtUtc = at
        };
        Db.AssistantMessages.Add(message);

        return message;
    }

    /// <summary>Заметка тестового раздела, существовавшая до разговора</summary>
    public Guid SeedNote(string title)
    {
        var id = Guid.CreateVersion7();
        Notes.Notes[id] = new TestNoteEntry(title, null);

        return id;
    }

    public Task Save() => Db.SaveChangesAsync(CancellationToken.None);

    /// <summary>
    /// Ещё один контекст той же базы — как у второй вкладки или второго экземпляра API:
    /// гонки проверяются только на разных соединениях
    /// </summary>
    public ApplicationContext NewContext() => new(new DbContextOptionsBuilder<ApplicationContext>()
        .UseNpgsql(Db.Database.GetConnectionString())
        .UseSnakeCaseNamingConvention()
        .Options);

    public Task<ApplyOutcome> Apply(AssistantPlan plan) =>
        Executor.Apply(plan.Id, Author, WorkspaceId, CancellationToken.None);

    /// <summary>Операция «завести заметку» — то, что кладёт в план <c>create_test_note</c></summary>
    public static PlanOperation CreateNote(string title, int index = 1, string? bookLink = null) => new()
    {
        Index = index,
        Kind = TestNoteKinds.CreateNote,
        Args = PlanJson.ToArgs(new CreateTestNoteArgs { Title = title, BookLink = bookLink })
    };

    /// <summary>Операция «завести книгу» — рамка плана</summary>
    public static PlanOperation CreateBook(string title, int index = 1) => new()
    {
        Index = index,
        Kind = TestNoteKinds.CreateBook,
        Args = PlanJson.ToArgs(new CreateTestBookArgs { Title = title })
    };

    /// <summary>Операция «переименовать заметку» — правка существующего</summary>
    public static PlanOperation RenameNote(Guid noteId, string title, int index = 1) => new()
    {
        Index = index,
        Kind = TestNoteKinds.RenameNote,
        Args = PlanJson.ToArgs(new RenameTestNoteArgs { NoteId = noteId.ToString(), Title = title })
    };

    /// <summary>Справка, которой нет: выпуска не было — «справка недоступна»</summary>
    public static HelpSearch EmptyHelp() => new(
        new ServiceCollection()
            .AddSingleton<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>,
                NullEmbedder>()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new HelpOptions { Path = Path.Combine(Path.GetTempPath(), "pettemplate-no-help") }),
        Options.Create(new SearchOptions()),
        NullLogger<HelpSearch>.Instance)
    {
        CacheDirectory = null
    };

    /// <summary>
    /// Достроить мир: реестр операций, копилка, исполнитель и инструменты — одни
    /// на мир, как в одной области команды
    /// </summary>
    private AssistantWorld Assemble()
    {
        var operations = new PlanOperations(
        [
            new CreateTestNoteOperation(Notes),
            new CreateTestBookOperation(Notes),
            new RenameTestNoteOperation(Notes)
        ]);
        var collector = new PlanCollector(operations);
        var executor = new PlanExecutor(Db, operations, new AssistantPermissions(Db), Context);
        var settings = Options.Create(Settings);

        Operations = operations;
        Collector = collector;
        Executor = executor;
        Tools = new AssistantTools(Help, Context, collector, executor, operations, Outcome, Memory,
            [new TestNoteTools(collector, Notes)], settings);

        return this;
    }
}
