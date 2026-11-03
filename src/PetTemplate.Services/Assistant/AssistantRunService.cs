using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Hooks;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Assistant.Tools;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Ход целиком (спец. assistant, раздел 3): промпт, история, цикл «модель →
/// инструменты → модель», разбор результата, предохранители, сообщение в ленту.
///
/// Цикл свой, а не <c>UseFunctionInvocation()</c> (15.2): внутрь него встраиваются
/// трасса по шагам, проверка «Остановить» и продление аренды команды перед каждым шагом,
/// эскалация бюджета и детектор повторов — всё то, что готовая обёртка прячет.
///
/// Режимов у хода нет: одношаговая просьба и долгое поручение идут одним и тем же кодом,
/// и разница между ними — только в том, сколько шагов цикл успеет сделать.
///
/// Метрики хода (13.4) — <see cref="AssistantMetrics"/>: спан прогона на старте,
/// длительность инструмента в вызове, итог хода и судьба плана в <see cref="Finish"/>.
/// </summary>
public class AssistantRunService(
    ApplicationContext context,
    IModelChatClients clients,
    AiModelCatalog models,
    ModelBreaker breaker,
    AssistantTools tools,
    AssistantContext runContext,
    PlanCollector collector,
    PlanExecutor executor,
    RunOutcome outcome,
    LoopGuards guards,
    SystemPromptBuilder prompts,
    HistoryBuilder history,
    MemoryService memory,
    IRunTrace trace,
    IRunBilling billing,
    RunNotifier notifier,
    AssistantMetrics metrics,
    AssistantPermissions permissions,
    IOptions<AssistantOptions> settings)
{
    /// <summary>
    /// Аргументы вызова в трассе: имена как прислала модель, кириллица без экранирования.
    /// Трасса в HTML без экранирования на клиенте не уходит
    /// </summary>
    private static readonly JsonSerializerOptions TraceArgumentsJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Клиент модели этого хода — ставится на старте попытки</summary>
    private IChatClient _chat = null!;

    /// <summary>Модель этой попытки и то, пробный ли это ход размыкателя</summary>
    private ModelPick _pick = null!;

    /// <summary>
    /// Исход модели уже отдан размыкателю: ему нужен первый ответ попытки,
    /// а не каждый — лишнего <c>UPDATE</c> на удачный шаг нет (16.2).
    /// Отказ, отданный ему, тоже исход: пробу он уже решил
    /// </summary>
    private bool _answered;

    /// <summary>
    /// Ходить было некуда: все модели разомкнуты или выключены. Ход кончается
    /// без единого токена
    /// </summary>
    private bool _unavailable;

    /// <param name="runId">Прогон, поставленный в очередь отправкой</param>
    /// <param name="extendLease">
    /// Продление аренды команды между шагами (раздел 3): ход в тридцать шагов идёт
    /// минуты, и без продления второй воркер подхватит команду, пока первый ещё думает,
    /// — два цикла пойдут по одному прогону. Обработчик очереди передаёт сюда свой
    /// способ продлить, сам сервис про очередь не знает
    /// </param>
    /// <param name="ct">Токен хода: «Остановить» и остановка воркера</param>
    public async Task Execute(Guid runId, Func<Task>? extendLease, CancellationToken ct)
    {
        var run = await context.AssistantRuns.SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run is not { Status: AssistantRunStatus.Running }) return;

        // Прогон — корневой спан, вызовы инструментов и обращения к модели — дочерние
        // (13.1). Текстов здесь нет: в телеметрию уходят структура и числа
        using var span = AssistantMetrics.Source.StartActivity("pettemplate.assistant.run");
        span?.SetTag("run.id", run.Id);

        // Автора убрали из пространства, пока ход ждал в очереди (раздел 9): от его имени
        // уже нельзя ни читать, ни собирать план. Модель не зовём — ход кончается строкой
        if (!await permissions.IsMember(run.UserId, run.WorkspaceId, ct))
        {
            await Finish(run, AssistantTexts.NotAMember, AssistantStopReason.Completed, ct);
            return;
        }

        // Модель выбирается на старте каждой попытки и записывается до первого
        // обращения к ней: повтор из очереди начинает ход заново и может уйти уже
        // к запасной (16.2), а разбору диалогов модель нужна и у хода без токенов
        var pick = await models.Pick(ct);
        if (pick == null)
        {
            _unavailable = true;
            await Finish(run, null, AssistantStopReason.ProviderError, ct);
            return;
        }

        _pick = pick;
        try
        {
            var model = pick.Model;
            run.AiModelId = model.Id;
            await context.SaveChangesAsync(ct);
            _chat = clients.For(model);

            Fill(run);
            runContext.UserText = await context.AssistantMessages
                .Where(x => x.RunId == run.Id && x.Role == AssistantMessageRole.User)
                .OrderByDescending(x => x.Id)
                .Select(x => x.Content)
                .FirstOrDefaultAsync(ct) ?? string.Empty;

            var thread = await history.Build(run.UserId, run.WorkspaceId, ct);
            // Наши инструкции — системным сообщением, данные людей — отдельным блоком
            // перед перепиской (раздел 9)
            var messages = new List<ChatMessage>((await Prompt(run, thread, ct)).Messages());
            messages.AddRange(thread.Messages);

            var catalog = tools.Catalog();
            var chatOptions = new ChatOptions { Tools = catalog };

            // Провайдер-специфичные поля едут рядом с запросом (16.5): folder id и scope
            // у российских поставщиков, `tool_stream` у некоторых других. Плоской тройки
            // «адрес, модель, ключ» хватает не всем, и место под остальное есть сразу
            var provider = settings.Value.Providers.GetValueOrDefault(model.Provider);
            if (provider is { ProviderOptions.Count: > 0 })
            {
                chatOptions.AdditionalProperties = [];
                foreach (var option in provider.ProviderOptions)
                    chatOptions.AdditionalProperties[option.Key] = option.Value;
            }

            var (answer, stop) = await Loop(messages, chatOptions, catalog, run, extendLease, ct);

            // «Остановить» пришло, пока модель писала финальный ответ: ответ уже есть,
            // но ход остановлен — и статус, и план-черновик такие же, как при остановке
            // между шагами (раздел 3), а не Done поверх Stopped
            if (stop == AssistantStopReason.Completed && await IsStopped(run.Id, ct))
                stop = AssistantStopReason.UserStopped;

            await Finish(run, answer, stop, ct);
        }
        finally
        {
            // Проба, о которой размыкателю так и не сказали ни «ответила», ни «отказ»:
            // ход оборвали до первого ответа или он упал не на модели. Держать модель
            // разомкнутой всю паузу из-за этого незачем — пробу отпускаем
            if (pick.Probe && !_answered) await breaker.Release(pick);
        }
    }

    /// <summary>
    /// Достроить ход, оборванный кнопкой прямо посреди запроса к модели (раздел 3):
    /// статус уже <c>Stopped</c>, а сообщения в ленте ещё нет — и пустоты там не бывает.
    ///
    /// Собранная часть плана сохраняется черновиком, без вопроса «делаем?»
    /// </summary>
    public async Task FinishStopped(Guid runId, CancellationToken ct) =>
        await FinishInterrupted(runId, AssistantStopReason.UserStopped, ct);

    /// <summary>
    /// Ход сорвался, и попытки очереди исчерпаны: прогон уходит в <c>Failed</c>,
    /// человек получает строку в ленте, а не молчание (12.3)
    /// </summary>
    public async Task FinishFailed(Guid runId, CancellationToken ct) =>
        await FinishInterrupted(runId, AssistantStopReason.ProviderError, ct);

    /// <summary>
    /// Сам цикл: модель → инструменты → модель, пока модель не ответит текстом,
    /// инструмент не решит исход хода или не сработает предохранитель
    /// </summary>
    private async Task<(string? Answer, AssistantStopReason Stop)> Loop(
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        IList<AITool> catalog,
        AssistantRun run,
        Func<Task>? extendLease,
        CancellationToken ct)
    {
        while (true)
        {
            // Статус перечитывается перед каждым обращением к модели и перед каждым
            // вызовом инструмента: это переживает несколько инстансов API (раздел 3)
            if (await IsStopped(run.Id, ct)) return (null, AssistantStopReason.UserStopped);

            if (!guards.CanContinue(out var limit)) return (null, limit!.Value);

            var response = await Ask(messages, chatOptions, run, ct);
            messages.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(x => x.Contents)
                .OfType<FunctionCallContent>()
                .ToArray();

            // Ответ без вызовов — это и есть финальный текст человеку
            if (calls.Length == 0) return (response.Text, AssistantStopReason.Completed);

            if (guards.ShouldEscalate())
            {
                run.Escalated = true;
                await context.SaveChangesAsync(ct);
                notifier.Notify(run.Id);
            }

            // Вызовы одного ответа исполняются последовательно: на параллельные
            // вызовы инструментов мы не закладываемся вовсе (16.5)
            foreach (var call in calls)
            {
                if (await IsStopped(run.Id, ct)) return (null, AssistantStopReason.UserStopped);

                messages.Add(await Invoke(call, catalog, run, extendLease, ct));

                // Вопрос задан или план применён — дальше инструментов не исполняем:
                // у хода уже есть исход, и модели остаётся написать строку
                if (outcome.Kind != RunOutcomeKind.None) break;

                if (!guards.CanContinue(out limit)) return (null, limit!.Value);
            }

            if (outcome.Kind != RunOutcomeKind.None)
            {
                // Один финальный ответ — уже без инструментов, чтобы модель не начала
                // новый круг поверх готового исхода
                var final = await Ask(messages, new ChatOptions(), run, ct);

                return (final.Text, AssistantStopReason.Completed);
            }
        }
    }

    private async Task FinishInterrupted(Guid runId, AssistantStopReason stop, CancellationToken ct)
    {
        var run = await context.AssistantRuns.SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run is not { FinishedAtUtc: null }) return;

        await Finish(run, null, stop, ct);
    }

    /// <summary>
    /// Контекст хода берётся из записи прогона, а не из запроса: ход просыпается
    /// в очереди через минуту или через час, и «завтра» обязано значить то же,
    /// что в момент просьбы (раздел 10)
    /// </summary>
    private void Fill(AssistantRun run)
    {
        runContext.UserId = run.UserId;
        runContext.WorkspaceId = run.WorkspaceId;
        runContext.TimeZone = run.TimeZone;
        runContext.Language = run.Language;
        runContext.NowUtc = run.StartedAtUtc;
        runContext.Today = DateOnly.FromDateTime(Local(run.StartedAtUtc, run.TimeZone));
        runContext.RunId = run.Id;
    }

    private static DateTime Local(DateTime utc, string timeZone)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc;
        }
    }

    private async Task<AssistantPrompt> Prompt(AssistantRun run, AssistantHistory thread, CancellationToken ct)
    {
        var workspaceName = await context.Workspaces
            .Where(x => x.Id == run.WorkspaceId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(ct) ?? string.Empty;

        var memberIds = context.UserInPermissions
            .Where(x => x.WorkspaceId == run.WorkspaceId)
            .Select(x => x.UserId);

        var members = await context.Profiles
            .AsNoTracking()
            .Where(x => memberIds.Contains(x.Id))
            .ToArrayAsync(ct);

        // Все факты пространства плюс личные автора хода — одним блоком, рядом
        // со списком участников, в каждом ходе, включая одношаговый (12.5)
        return prompts.Build(
            runContext,
            workspaceName,
            members,
            await memory.ForPrompt(run.UserId, run.WorkspaceId, ct),
            tools.Instructions(),
            thread.PendingPlan,
            await QuestionStreak(run, ct),
            thread.Summary);
    }

    /// <summary>
    /// Сколько уточняющих вопросов подряд задано без движения в деле (раздел 8).
    /// «Не сдвинулось» меряется кодом: подряд идущие ходы, закончившиеся вопросом,
    /// у которых черновик плана не изменился. На четвёртом ассистент обязан показать
    /// план или сказать прямо, что не понял просьбу — это ему говорит промпт
    /// </summary>
    private async Task<int> QuestionStreak(AssistantRun run, CancellationToken ct)
    {
        var tail = await context.AssistantMessages
            .Where(x => x.UserId == run.UserId
                        && x.WorkspaceId == run.WorkspaceId
                        && x.Role == AssistantMessageRole.Assistant)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(5)
            .Select(x => new { x.Id, x.Options, x.PlanId })
            .ToArrayAsync(ct);

        var streak = 0;
        string? previousDraft = null;

        foreach (var message in tail.OrderByDescending(x => x.Id))
        {
            // Не вопрос — значит, между вопросами что-то происходило, и подряд они не идут
            if (string.IsNullOrEmpty(message.Options)) break;

            var draft = message.PlanId == null
                ? string.Empty
                : await context.AssistantPlans
                    .Where(x => x.Id == message.PlanId)
                    .Select(x => x.Operations)
                    .SingleOrDefaultAsync(ct) ?? string.Empty;

            if (previousDraft != null && draft != previousDraft) break;

            previousDraft = draft;
            streak++;
        }

        return streak;
    }

    private async Task<ChatResponse> Ask(
        List<ChatMessage> messages,
        ChatOptions options,
        AssistantRun run,
        CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            response = await _chat.GetResponseAsync(messages, options, ct);
        }
        catch (Exception e)
        {
            // Шаг записывается и упавший: трасса нужна прежде всего у него.
            // Само исключение уходит наверх — повторит очередь (раздел 3)
            await trace.Write(new RunStep(run.Id, guards.Step, RunStep.Model, null, Last(messages), null,
                0, 0, (int)started.ElapsedMilliseconds, e.Message), ct);

            // Переключение — на границе хода (16.2): эта попытка падает целиком,
            // а повтор из очереди уже найдёт модель разомкнутой и уйдёт к следующей.
            // Пробу решает первый ответ попытки: удалась — модель уже замкнута, и отказ
            // на следующем шаге — обычный отказ в счёт подряд идущих, а не провал пробы
            await breaker.Failed(_answered ? _pick with { Probe = false } : _pick, e, ct);
            _answered = true;

            throw;
        }

        if (!_answered)
        {
            _answered = true;
            await breaker.Succeeded(_pick);
        }

        guards.AddTokens(response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);

        // Токены прогона копятся по всем попыткам, а не по одной: упавшая попытка
        // тоже израсходовала их у поставщика. Уезжают ближайшим сохранением
        run.TokensIn += response.Usage?.InputTokenCount ?? 0;
        run.TokensCached += response.Usage?.CachedInputTokenCount ?? 0;
        run.TokensOut += response.Usage?.OutputTokenCount ?? 0;

        await trace.Write(new RunStep(run.Id, guards.Step, RunStep.Model, null, Last(messages), response.Text,
            response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0,
            (int)started.ElapsedMilliseconds, null), ct);

        return response;
    }

    /// <summary>
    /// Один шаг: вызов инструмента. Перед ним номер шага и имя инструмента уезжают
    /// в базу — из них лента рисует плашку «шаг 7 из 30, ищу в справке» (раздел 3)
    /// </summary>
    private async Task<ChatMessage> Invoke(
        FunctionCallContent call,
        IList<AITool> catalog,
        AssistantRun run,
        Func<Task>? extendLease,
        CancellationToken ct)
    {
        run.Step = guards.NextStep();
        run.CurrentActivity = call.Name.Length > 64 ? call.Name[..64] : call.Name;
        await context.SaveChangesAsync(ct);

        // Долгий опрос ждёт именно этого: номер шага и имя инструмента — всё,
        // из чего лента рисует плашку (раздел 14)
        notifier.Notify(run.Id);

        if (extendLease != null) await extendLease();

        using var span = AssistantMetrics.Source.StartActivity("pettemplate.assistant.tool");
        span?.SetTag("tool.name", call.Name);

        // Кириллица — как есть, без \uXXXX: аргументы читают люди — «Диалоги» в админке
        // (спец. admin, 16.2), — а потолок шага трассы в 4 КБ экранирование съедало вшестеро
        // быстрее. Строка же — ключ детектора повторов: одинаковые вызовы дают одинаковый ключ
        var arguments = JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object?>(), TraceArgumentsJson);
        var started = Stopwatch.StartNew();
        string result;
        string? error = null;

        var function = catalog.OfType<AIFunction>().SingleOrDefault(x => x.Name == call.Name);
        if (function == null)
        {
            // Модель придумала инструмент: это разговор, а не падение — пусть выбирает
            // из каталога заново
            result = $"инструмента «{call.Name}» нет, посмотри список доступных";
            error = result;
        }
        else
        {
            using var timeout = guards.ToolTimeout(ct);
            try
            {
                var invoked = await function.InvokeAsync(new AIFunctionArguments(call.Arguments), timeout.Token);
                result = invoked?.ToString() ?? string.Empty;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result = "инструмент не ответил вовремя — попробуй другой подход";
                error = result;
            }
        }

        result = guards.Decorate(call.Name, arguments, result);

        metrics.Tool(call.Name, started.Elapsed.TotalMilliseconds);

        await trace.Write(new RunStep(run.Id, guards.Step, RunStep.Tool, call.Name, arguments, result,
            0, 0, (int)started.ElapsedMilliseconds, error), ct);

        return new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, result)]);
    }

    /// <summary>
    /// Статус перечитывается из базы, а не из отслеживаемой сущности: «Остановить»
    /// мог прийти в другой инстанс API (раздел 3)
    /// </summary>
    private async Task<bool> IsStopped(Guid runId, CancellationToken ct) =>
        await context.AssistantRuns
            .Where(x => x.Id == runId)
            .Select(x => x.Status)
            .SingleOrDefaultAsync(ct) == AssistantRunStatus.Stopped;

    /// <summary>
    /// Конец хода: сообщение в ленту, план — если он собрался, — и запись прогона.
    ///
    /// Пустого сообщения не бывает (раздел 3): не успел ничего — так и пишет одной
    /// строкой. Ход, оборванный по лимиту, обязан сдать частичный результат так же,
    /// как оборванный кнопкой (раздел 8)
    /// </summary>
    private async Task Finish(AssistantRun run, string? answer, AssistantStopReason stop, CancellationToken ct)
    {
        // Учёт расхода — в той же транзакции, что и финальный статус (13.5).
        // Транзакцию «Остановить» уже не обрывает: оборванная между сохранением
        // и фиксацией, она оставила бы прогон в памяти законченным, а в базе — идущим
        await using var transaction = billing.ShouldCharge(run)
            ? await NestedTransaction.Begin(context.Database, CancellationToken.None)
            : null;
        await billing.Charge(run, CancellationToken.None);

        // «Очистить», пока ход ещё шёл (12.1.1): переписку стёрли после старта хода, и ни
        // сообщения, ни плана, ни дописанной трассы в пустой ленте появиться не должно.
        // Прогон и его расход записываются как обычно
        var cleared = await Cleared(run.Id, ct);

        var planId = cleared ? null : await SavePlan(run, stop, ct);

        // Одно «сейчас» на сообщение и прогон: отметки одного события не расходятся
        var now = DateTime.UtcNow;
        var message = new AssistantMessage
        {
            Id = Guid.CreateVersion7(now),
            UserId = run.UserId,
            WorkspaceId = run.WorkspaceId,
            Role = AssistantMessageRole.Assistant,
            // Вопрос в ленте — последняя реплика модели (5.2). Промолчала — показываем сам
            // вопрос из `ask_user`, а не «модель не отозвалась» над вариантами ответа
            Content = Content(
                outcome.Kind == RunOutcomeKind.AskUser && string.IsNullOrWhiteSpace(answer) ? outcome.Question : answer,
                stop),
            RunId = run.Id,
            PlanId = planId,
            Options = outcome.Kind == RunOutcomeKind.AskUser && outcome.Options.Count > 0
                ? JsonSerializer.Serialize(outcome.Options)
                : null,
            FactIds = outcome.FactIds.Count > 0 ? JsonSerializer.Serialize(outcome.FactIds) : null,
            CreatedAtUtc = now
        };
        if (cleared)
        {
            await context.AssistantTraceSteps
                .Where(x => x.RunId == run.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
        else
        {
            await context.AssistantMessages.AddAsync(message, ct);
        }

        run.Status = stop switch
        {
            AssistantStopReason.UserStopped => AssistantRunStatus.Stopped,
            // Провайдер не ответил, и попытки очереди кончились — это именно сбой,
            // а не законченный ход (12.3)
            AssistantStopReason.ProviderError => AssistantRunStatus.Failed,
            _ => AssistantRunStatus.Done
        };
        run.StopReason = stop;
        run.CurrentActivity = null;
        run.FinishedAtUtc = now;

        await context.SaveChangesAsync(CancellationToken.None);
        if (transaction != null) await transaction.CommitAsync(CancellationToken.None);

        metrics.Run(
            stop,
            run.Escalated,
            run.Step,
            run.TokensIn,
            run.TokensOut,
            (now - run.StartedAtUtc).TotalMilliseconds);

        // Судьба плана — главная метрика фичи: дошёл ли ход до согласованного плана
        if (planId.HasValue) metrics.Plan(PlanOutcome(stop));

        // Что факт пригодился, считается по самим текстам хода — подстрочно,
        // без модели и без отдельного инструмента (12.5). Ход уже записан: счётчик —
        // не повод ронять конец хода, поэтому и токен здесь не ходовой
        if (!cleared)
            await memory.TouchUsed(run.UserId, run.WorkspaceId, runContext.UserText, message.Content,
                CancellationToken.None);

        // Конец хода — единственное, что клиенту действительно нужно узнать быстро:
        // уведомлений в продукте нет, и лента меняется только по этому ответу (раздел 3)
        notifier.Notify(run.Id);
    }

    /// <summary>
    /// Стёрли ли тред, пока ход шёл (12.1.1): пометку ставит «Очистить» до того, как
    /// стереть переписку. Читается из базы, а не из отслеживаемой сущности — «Очистить»
    /// пришло другим запросом
    /// </summary>
    private async Task<bool> Cleared(Guid runId, CancellationToken ct) =>
        await context.AssistantRuns
            .Where(x => x.Id == runId)
            .Select(x => x.Cleared)
            .SingleOrDefaultAsync(ct);

    /// <summary>
    /// Какой план приедет с сообщением. Собранные инструментами операции сохраняются
    /// планом (12.2.1); применённый уже сохранён — сообщению достаётся его идентификатор,
    /// по нему клиент рисует отчёт
    /// </summary>
    private async Task<Guid?> SavePlan(AssistantRun run, AssistantStopReason stop, CancellationToken ct)
    {
        if (outcome.Kind == RunOutcomeKind.PlanApplied) return outcome.AppliedPlanId;
        if (collector.Count == 0) return null;

        // Незакрытый план в треде один: новый отменяет предыдущий (12.2.1)
        await executor.CancelOpen(run.UserId, run.WorkspaceId, ct);

        // Ход кончился вопросом, остановкой или сбоем — план собран не до конца, и спрашивать
        // «делаем?» по нему нельзя: он Draft (5.2, раздел 3). Сбой — тем более: модель
        // не дописала ни план, ни вопрос, а «да» на следующую реплику применило бы обрывок
        var draft = outcome.Kind == RunOutcomeKind.AskUser
                    || stop is AssistantStopReason.UserStopped or AssistantStopReason.ProviderError;

        var plan = new AssistantPlan
        {
            Id = Guid.CreateVersion7(),
            UserId = run.UserId,
            WorkspaceId = run.WorkspaceId,
            RunId = run.Id,
            Status = draft ? AssistantPlanStatus.Draft : AssistantPlanStatus.Pending,
            Operations = PlanJson.Serialize(new PlanDocument { Operations = [.. collector.Operations] }),
            CreatedAtUtc = DateTime.UtcNow
        };
        await context.AssistantPlans.AddAsync(plan, ct);

        return plan.Id;
    }

    private string PlanOutcome(AssistantStopReason stop) => outcome.Kind switch
    {
        RunOutcomeKind.PlanApplied when outcome.Applied?.Kind == ApplyOutcomeKind.Applied => PlanOutcomes.Applied,
        // Не сошлось с базой — ассистент пересобирает план и спрашивает снова (5.4)
        RunOutcomeKind.PlanApplied => PlanOutcomes.RebuiltAfterYesBut,
        _ => stop == AssistantStopReason.Completed ? PlanOutcomes.Proposed : PlanOutcomes.Cancelled
    };

    private string Content(string? answer, AssistantStopReason stop)
    {
        var text = (answer ?? string.Empty).Trim();

        if (stop == AssistantStopReason.Completed)
            return text.Length > 0 ? text : AssistantTexts.ProviderError;

        if (_unavailable) return AssistantTexts.ModelUnavailable;

        var prefix = stop switch
        {
            AssistantStopReason.ProviderError => AssistantTexts.ProviderError,
            AssistantStopReason.UserStopped => text.Length > 0 ? AssistantTexts.Stopped : AssistantTexts.StoppedWithNothing,
            AssistantStopReason.TokenLimit => AssistantTexts.TokenLimit,
            _ => text.Length > 0 ? AssistantTexts.StepLimit : AssistantTexts.StepLimitWithNothing
        };

        var tail = collector.Count > 0 && stop == AssistantStopReason.UserStopped
            ? "\n\n" + AssistantTexts.DraftAfterStop
            : string.Empty;

        return text.Length > 0 ? $"{prefix}\n\n{text}{tail}" : prefix + tail;
    }

    /// <summary>
    /// В трассу пишется последнее, что ушло в модель: весь контекст туда не влезет,
    /// а интересен именно последний ход мысли
    /// </summary>
    private static string Last(List<ChatMessage> messages) =>
        messages.Count == 0 ? string.Empty : messages[^1].Text;
}
