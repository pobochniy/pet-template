using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Hooks;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Assistant.Tools;
using PetTemplate.Services.Help;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant.Check;

/// <summary>
/// Одна фраза сверки — один настоящий ход в изолированном мире (спец. admin, 15.3):
/// тот же цикл, те же инструменты, та же сборка промпта, что в бою, но поверх базы
/// в памяти и своего контейнера сервисов, который живёт ровно одну фразу.
///
/// Изоляция — не вежливость, а условие: в боевую базу из мира сверки не уходит ни
/// строчки. Прогон, план, память, размыкатель — всё в памяти; трассы и учёта хода нет
/// вовсе (<see cref="NullRunTrace"/>, <see cref="NullRunBilling"/>) — расход фразы
/// пишет сама сверка служебной строкой. Метрики хода уходят под своим именем, которое
/// приложение не публикует; спаны — под корневым спаном сверки. Наружу идёт только
/// запрос к модели — ради него сверка и делается.
///
/// База в памяти — <c>Microsoft.EntityFrameworkCore.InMemory</c>: Postgres
/// ради одной фразы не нужен, а атомарные <c>UPDATE</c> размыкателя в мире сверки
/// не случаются (копия модели замкнута) или глотаются им же (запись отказа).
///
/// Инструменты — ядра: <c>search_help</c>, <c>ask_user</c>, <c>remember_fact</c>.
/// Раздел проекта со своими инструментами (спец. assistant, 7.1) регистрирует их здесь же,
/// в <see cref="Build"/>, и сеет данные под свои фразы в <see cref="ModelCheckWorld"/>
/// </summary>
public static class ModelCheckRunner
{
    /// <summary>Под этим именем метрики мира сверки никто не слушает</summary>
    public const string MeterName = "PetTemplate.ModelCheck";

    /// <summary>Зона, в которой фразы набора считают «завтра» и «в пятницу» (спец. assistant, 10)</summary>
    public const string TimeZone = "Europe/Moscow";

    /// <param name="phrase">Фраза набора</param>
    /// <param name="model">
    /// Строка справочника, которую сверяют. В мир едет её копия со сброшенным
    /// размыкателем: состояние боевой строки сверку не останавливает и ею не меняется
    /// </param>
    /// <param name="chat">Клиент модели; писарь поверх него — свой у каждой фразы</param>
    /// <param name="assistant">
    /// Настройки хода — бюджет шагов, поля поставщика, — как в бою. Копируются: мир
    /// не должен менять настройки приложения
    /// </param>
    /// <param name="search">Настройки эмбеддера — справка мира ищет так же, как боевая</param>
    /// <param name="embedder">Эмбеддер приложения — вектор запроса к справке</param>
    /// <param name="help">
    /// Справка мира — та, что сейчас в памяти API. Нет — «справка недоступна», и фраза
    /// проверяет только, что её позвали
    /// </param>
    /// <param name="extendLease">Продление аренды команды между шагами, как у хода</param>
    /// <param name="ct">Остановка воркера</param>
    public static async Task<ModelCheckOutcome> RunOne(
        ModelCheckPhrase phrase,
        AiModel model,
        IChatClient chat,
        AssistantOptions assistant,
        SearchOptions search,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        HelpIndex? help,
        Func<Task>? extendLease,
        CancellationToken ct)
    {
        // Спаны хода источник пишет общий (он статический), поэтому фраза сверки идёт под
        // своим корневым спаном: в трассах выдуманный ход виден как часть сверки, а не как
        // ход человека
        using var span = AssistantMetrics.Source.StartActivity("pettemplate.assistant.model_check");
        span?.SetTag("model.id", model.Id);

        var recorder = new RecordingChatClient(chat);
        await using var provider = Build(recorder, assistant, search, embedder);
        if (help != null) provider.GetRequiredService<HelpSearch>().Use(help);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var context = services.GetRequiredService<ApplicationContext>();
        var world = ModelCheckWorld.Seed(context);

        // Справочник из одной строки — той модели, что сверяется: ход без справочника
        // не пойдёт, а выключенную модель сверяют так же, как включённую
        context.AiModels.Add(new AiModel
        {
            Id = model.Id,
            Provider = model.Provider,
            Key = model.Key,
            DisplayName = model.DisplayName,
            Priority = 1,
            CostIn = model.CostIn,
            CostCached = model.CostCached,
            CostOut = model.CostOut,
            IsEnabled = true,
            UpdatedUtc = DateTime.UtcNow
        });

        var now = DateTime.UtcNow;
        var run = new AssistantRun
        {
            Id = Guid.CreateVersion7(now),
            UserId = world.UserId,
            WorkspaceId = world.WorkspaceId,
            TimeZone = TimeZone,
            Language = "ru",
            Status = AssistantRunStatus.Running,
            StartedAtUtc = now
        };
        context.AssistantRuns.Add(run);
        context.AssistantMessages.Add(new AssistantMessage
        {
            Id = Guid.CreateVersion7(now),
            UserId = world.UserId,
            WorkspaceId = world.WorkspaceId,
            Role = AssistantMessageRole.User,
            Content = phrase.Phrase,
            RunId = run.Id,
            CreatedAtUtc = now
        });
        await context.SaveChangesAsync(ct);

        try
        {
            await services.GetRequiredService<AssistantRunService>().Execute(run.Id, extendLease, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return ModelCheckOutcome.Failed(phrase, recorder, e.Message);
        }

        var answer = await context.AssistantMessages
            .Where(x => x.RunId == run.Id && x.Role == AssistantMessageRole.Assistant)
            .Select(x => x.Content)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return ModelCheckOutcome.From(phrase, recorder, answer, run);
    }

    /// <summary>
    /// Минимальная проводка: то же, что в <c>ServicesExtensions.AddAssistant</c>,
    /// но без веб-хоста, очереди и Postgres — фразе нужен один ход, а не приложение
    /// </summary>
    private static ServiceProvider Build(
        RecordingChatClient recorder,
        AssistantOptions assistant,
        SearchOptions search,
        IEmbeddingGenerator<string, Embedding<float>> embedder)
    {
        var services = new ServiceCollection();

        services.AddLogging(x => x.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ApplicationContext>(x => x
            .UseInMemoryDatabase($"model-check-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Copy(assistant)));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ModelBreakerOptions()));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new HelpOptions()));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(search));

        services.AddSingleton(recorder);
        services.AddSingleton<IModelChatClients>(new RecorderClients(recorder));
        services.AddSingleton(embedder);
        services.AddSingleton<RunNotifier>();
        services.AddSingleton(new AssistantMetrics(MeterName));
        // Справка мира кэша векторов на диске не держит: индекс уже собран боевой справкой
        services.AddSingleton(sp => new HelpSearch(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<HelpOptions>>(),
            sp.GetRequiredService<IOptions<SearchOptions>>(),
            sp.GetRequiredService<ILogger<HelpSearch>>(),
            sp.GetRequiredService<AssistantMetrics>()) { CacheDirectory = null });

        services.AddTransient<AiModelCatalog>();
        services.AddTransient<ModelBreaker>();
        services.AddScoped<AssistantContext>();
        services.AddScoped<PlanCollector>();
        services.AddScoped<RunOutcome>();
        services.AddScoped<LoopGuards>();
        services.AddScoped<AssistantTools>();
        services.AddScoped<AssistantRunService>();
        services.AddTransient<MemoryService>();
        services.AddTransient<PlanOperations>();
        services.AddTransient<PlanExecutor>();
        services.AddTransient<AssistantPermissions>();
        services.AddTransient<SystemPromptBuilder>();
        services.AddTransient<HistoryBuilder>();
        services.AddScoped<IRunTrace, NullRunTrace>();
        services.AddScoped<IRunBilling, NullRunBilling>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Копия через JSON: у настроек вложенные словари поставщиков, и поверхностная
    /// копия делила бы их с приложением. Словарь поставщиков пересобирается без учёта
    /// регистра, как в оригинале: JSON сравнение ключей не переносит
    /// </summary>
    private static AssistantOptions Copy(AssistantOptions options)
    {
        var copy = JsonSerializer.Deserialize<AssistantOptions>(JsonSerializer.Serialize(options))!;
        copy.Providers = new Dictionary<string, AssistantProviderOptions>(copy.Providers, StringComparer.OrdinalIgnoreCase);

        return copy;
    }

    /// <summary>Справочник мира из одной строки, и отвечает на неё писарь</summary>
    private sealed class RecorderClients(RecordingChatClient recorder) : IModelChatClients
    {
        public IChatClient For(AiModel model) => recorder;
    }
}
