using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Hooks;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Help;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant.Check;

/// <summary>
/// Одна фраза сверки — один настоящий ход в изолированном мире (спец. admin, 15.3):
/// тот же цикл, те же инструменты, та же сборка промпта, что в бою, — та же сборка
/// <see cref="AssistantRuntime.AddAssistantRuntime"/> и те же разделы проекта, — но
/// в своём контейнере сервисов, который живёт ровно одну фразу.
///
/// Изоляция — не вежливость, а условие: в боевую базу из мира сверки не уходит ни
/// строчки. Мир — настоящий Postgres, но внутри одной транзакции, которую откатывают
/// после фразы: прогон, план, память, люди и копия модели живут только в ней и другим
/// соединениям не видны. Транзакции, которые открывает сам ход (применение плана, конец
/// хода), становятся точками сохранения внутри неё (<see cref="NestedTransaction"/>),
/// а атомарные <c>UPDATE</c> размыкателя работают как в бою — по копии модели.
///
/// Трассы и учёта хода нет вовсе (<see cref="NullRunTrace"/>, <see cref="NullRunBilling"/>) —
/// расход фразы пишет сама сверка служебной строкой. Метрики хода уходят под своим
/// именем, которое приложение не публикует; спаны — под корневым спаном сверки. Наружу
/// идёт только запрос к модели — ради него сверка и делается.
///
/// Раздел проекта (спец. assistant, 7.1) попадает сюда сам: его инструменты и операции —
/// тем же <c>IAssistantSection.Register</c>, данные под фразы — <c>IAssistantSection.Seed</c>
/// </summary>
public static class ModelCheckRunner
{
    /// <summary>Под этим именем метрики мира сверки никто не слушает</summary>
    public const string MeterName = "PetTemplate.ModelCheck";

    /// <summary>Зона, в которой фразы набора считают «завтра» и «в пятницу» (спец. assistant, 10)</summary>
    public const string TimeZone = "Europe/Moscow";

    /// <summary>
    /// Порядок копии модели в справочнике мира: первее любой настоящей строки. Боевые
    /// модели внутри транзакции мира видны, и ход обязан выбрать копию, а не их — иначе
    /// размыкатель мира писал бы в боевую строку
    /// </summary>
    private const int CopyPriority = int.MinValue;

    /// <param name="phrase">Фраза набора</param>
    /// <param name="model">
    /// Строка справочника, которую сверяют. В мир едет её копия под своим id и со
    /// сброшенным размыкателем: состояние боевой строки сверку не останавливает и ею
    /// не меняется
    /// </param>
    /// <param name="chat">Клиент модели; писарь поверх него — свой у каждой фразы</param>
    /// <param name="environment">Настройки и сервисы приложения, которые мир берёт как есть</param>
    /// <param name="extendLease">Продление аренды команды между шагами, как у хода</param>
    /// <param name="ct">Остановка воркера</param>
    public static async Task<ModelCheckOutcome> RunOne(
        ModelCheckPhrase phrase,
        AiModel model,
        IChatClient chat,
        ModelCheckEnvironment environment,
        Func<Task>? extendLease,
        CancellationToken ct)
    {
        // Спаны хода источник пишет общий (он статический), поэтому фраза сверки идёт под
        // своим корневым спаном: в трассах выдуманный ход виден как часть сверки, а не как
        // ход человека
        using var span = AssistantMetrics.Source.StartActivity("pettemplate.assistant.model_check");
        span?.SetTag("model.id", model.Id);

        var recorder = new RecordingChatClient(chat);
        await using var provider = Build(recorder, environment);
        if (environment.Help != null) provider.GetRequiredService<HelpSearch>().Use(environment.Help);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var context = services.GetRequiredService<ApplicationContext>();

        // Весь мир — одна транзакция, и она не фиксируется никогда
        await using var world = await context.Database.BeginTransactionAsync(ct);
        try
        {
            return await Run(phrase, model, recorder, environment, services, context, extendLease, ct);
        }
        finally
        {
            await world.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task<ModelCheckOutcome> Run(
        ModelCheckPhrase phrase,
        AiModel model,
        RecordingChatClient recorder,
        ModelCheckEnvironment environment,
        IServiceProvider services,
        ApplicationContext context,
        Func<Task>? extendLease,
        CancellationToken ct)
    {
        var (userId, workspaceId) = await ModelCheckWorld.Seed(context, ct);
        foreach (var section in environment.Sections) await section.Seed(context, workspaceId, userId, ct);

        // Справочник мира — копия той модели, что сверяется: ход без справочника
        // не пойдёт, а выключенную модель сверяют так же, как включённую
        context.AiModels.Add(new AiModel
        {
            Id = Guid.CreateVersion7(),
            Provider = model.Provider,
            Key = model.Key,
            DisplayName = model.DisplayName,
            Priority = CopyPriority,
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
            UserId = userId,
            WorkspaceId = workspaceId,
            TimeZone = TimeZone,
            Language = "ru",
            Status = AssistantRunStatus.Running,
            StartedAtUtc = now
        };
        context.AssistantRuns.Add(run);
        context.AssistantMessages.Add(new AssistantMessage
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            WorkspaceId = workspaceId,
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
    /// Сборка хода — та же, что у приложения, и разделы те же; поверх — то, чем мир
    /// отличается: своя база в транзакции, писарь вместо клиентов моделей, свои
    /// метрики, ни трассы, ни учёта. Веб-хоста и очереди нет — фразе нужен один ход
    /// </summary>
    private static ServiceProvider Build(RecordingChatClient recorder, ModelCheckEnvironment environment)
    {
        var services = new ServiceCollection();

        services.AddAssistantRuntime();
        foreach (var section in environment.Sections) services.AddAssistantSection(section);

        services.AddLogging(x => x.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ApplicationContext>(x => ApplicationContextFactory.Configure(x, environment.ConnectionString));

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Copy(environment.Assistant)));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ModelBreakerOptions()));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new HelpOptions()));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(environment.Search));

        services.AddSingleton(recorder);
        services.AddSingleton(environment.Embedder);
        services.Replace(ServiceDescriptor.Singleton<IModelChatClients>(new RecorderClients(recorder)));
        services.Replace(ServiceDescriptor.Singleton(new AssistantMetrics(MeterName)));
        services.Replace(ServiceDescriptor.Scoped<IRunTrace, NullRunTrace>());
        services.Replace(ServiceDescriptor.Scoped<IRunBilling, NullRunBilling>());

        // Справка мира кэша векторов на диске не держит: индекс уже собран боевой справкой
        services.AddSingleton(sp => new HelpSearch(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<HelpOptions>>(),
            sp.GetRequiredService<IOptions<SearchOptions>>(),
            sp.GetRequiredService<ILogger<HelpSearch>>(),
            sp.GetRequiredService<AssistantMetrics>()) { CacheDirectory = null });

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
