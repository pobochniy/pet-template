using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Тред и очередь ходов (спец. assistant, разделы 3 и 12.1).
///
/// Тред — пара «человек + пространство»; ходов у него одновременно не больше одного.
/// Очередь команд не потредовая: с несколькими воркерами вторую команду подхватили бы
/// параллельно первой, и два цикла писали бы в один тред. Поэтому при живом прогоне
/// команда <b>не ставится</b> вовсе — сообщение просто ждёт своей очереди, а поставит
/// её обработчик, закончив предыдущий ход. Держит это частичный уникальный индекс
/// «один незаконченный ход на тред», а не только проверка перед вставкой: живым ход
/// остаётся и после «Остановить», пока обработчик не допишет его конец.
/// </summary>
public class AssistantThreadService(
    ApplicationContext context,
    RunCancellation cancellation,
    WorkspaceLimits limits,
    AiModelCatalog models,
    MemoryService memory,
    BillingMetrics billing,
    TraceSummary trace,
    IOptions<AssistantOptions> options)
{
    /// <summary>
    /// Сообщение человека. Возвращает прогон, за которым клиенту следить: новый —
    /// или тот, что идёт сейчас, если ассистент ещё не ответил.
    ///
    /// Лимит расхода пространства исчерпан — отказ кодом (13.6). Это единственная
    /// проверка лимита во всём ходе: расход считается вместе с резервом идущих и ждущих
    /// ходов (<see cref="WorkspaceLimits.Reserved"/>), а прогон или ждущая реплика,
    /// записанные здесь, сами становятся резервом. Что сюда прошло, исполняется до конца:
    /// ни воркер при подхвате, ни очередь треда второй раз не спрашивают — резерв
    /// под реплику занят уже здесь. Проверка стоит и перед репликой в очередь треда: отказ «лимит исчерпан» должен
    /// прийти на то сообщение, которое его упёрло, а не молча отложиться.
    ///
    /// Раньше лимита проверяется, есть ли куда идти: все модели разомкнуты или выключены —
    /// отказ <c>modelUnavailable</c> (16.2), ни реплики, ни хода. Просить подождать до
    /// завтра, когда ассистент всё равно не ответит, хуже, чем сказать как есть. Проверка
    /// только читает: модель может разомкнуться и после неё — тогда ход кончится той же
    /// строкой «ассистент сейчас недоступен» в ленте
    /// </summary>
    public async Task<(Guid RunId, Guid MessageId)> Send(
        Guid userId,
        Guid workspaceId,
        string text,
        string timeZone,
        CancellationToken ct)
    {
        // Одно «сейчас» на проверку лимита, реплику и прогон: это одно событие
        var now = DateTime.UtcNow;

        if (!await models.AnyAvailable(ct)) throw new BadRequestException(AssistantErrors.ModelUnavailable);

        // Проверка и резерв — под замком лимита до конца метода (13.6): прогон или ждущая
        // реплика, записанные ниже, и есть резерв, и соседний Send должен его увидеть
        await using var hold = await limits.Hold(workspaceId, userId, ct);
        await CheckLimit(userId, workspaceId, now, ct);

        // Язык ответа — язык интерфейса автора (раздел 17). Спрашиваем профиль здесь,
        // а не в контроллере: прогон обязан запомнить и его, и зону на момент просьбы
        var language = await context.Profiles
            .Where(x => x.Id == userId)
            .Select(x => x.Language)
            .SingleOrDefaultAsync(ct) ?? LanguageConstants.Default;

        var message = new AssistantMessage
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            WorkspaceId = workspaceId,
            Role = AssistantMessageRole.User,
            Content = text,
            CreatedAtUtc = now
        };

        var running = await Running(userId, workspaceId, ct);
        if (running != null) return (running.Id, await Wait(message, ct));

        var run = Start(userId, workspaceId, timeZone, language, now);
        message.RunId = run.Id;

        await context.AssistantRuns.AddAsync(run, ct);
        await context.AssistantMessages.AddAsync(message, ct);
        // Команда уезжает тем же SaveChanges, что и сообщение с прогоном (outbox):
        // либо в базе есть и ход, и задание на него, либо ни того, ни другого
        context.Enqueue(new AssistantRunCommand { RunId = run.Id });

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsDuplicate(e, DbErrors.AssistantRunningIndex))
        {
            // Две отправки с двух вкладок: живой ход завела соседняя, а эта реплика
            // встаёт в очередь треда.
            // Свой прогон и его команда уходят из трекера; связь, которую трекер успел
            // проставить реплике, — тоже, иначе прогон вернулся бы вместе с ней
            context.ChangeTracker.Clear();
            message.Run = null;
            message.RunId = null;
            var messageId = await Wait(message, ct);

            // Соседний ход мог успеть закончиться — тогда реплику не подхватит никто,
            // кроме нас самих
            var started = await StartNextWaiting(userId, workspaceId, ct);
            var current = await Running(userId, workspaceId, ct);

            return (started ?? current?.Id ?? Guid.Empty, messageId);
        }

        return (run.Id, message.Id);
    }

    /// <summary>
    /// Лимиты на входе (13.6): установки, пространства и человека. Голос проверяет их тем же методом до
    /// расшифровки (6.1): распознавание — тоже обращение к поставщику за счёт пространства,
    /// и его расход идёт в тот же лимит
    /// </summary>
    public async Task CheckLimit(Guid userId, Guid workspaceId, DateTime now, CancellationToken ct)
    {
        var refused = await limits.Check(workspaceId, userId, now, ct);
        billing.Send(refused != null);
        if (refused == null) return;

        // Имя пространства — только в отказе лимитом пространства: потолок человека
        // и установки к пространству не относятся
        if (refused != AssistantErrors.DailyLimitReached && refused != AssistantErrors.MonthlyLimitReached)
            throw new BadRequestException(refused);

        var workspace = await context.Workspaces
            .Where(x => x.Id == workspaceId)
            .Select(x => x.Name)
            .SingleAsync(ct);

        throw new BadRequestException(refused.With(("workspace", workspace)));
    }

    /// <summary>
    /// Первое ждущее сообщение треда получает свой ход. Зовётся обработчиком, когда
    /// предыдущий ход закончился: очередь треда держится на этом, а не на очереди команд
    /// </summary>
    public async Task<Guid?> StartNextWaiting(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        if (await Running(userId, workspaceId, ct) != null) return null;

        // Человека убрали из пространства — его ждущие реплики ходов не получают (раздел 9):
        // иначе очередь треда крутила бы по ходу на реплику, и каждый кончался бы отказом
        if (!await context.UserInPermissions.AnyAsync(x => x.UserId == userId && x.WorkspaceId == workspaceId, ct))
            return null;

        var waiting = await context.AssistantMessages
            .Where(x => x.UserId == userId
                        && x.WorkspaceId == workspaceId
                        && x.Role == AssistantMessageRole.User
                        && x.RunId == null)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);
        if (waiting == null) return null;

        // Зону и язык берём у прошлого хода этого же треда: сообщение их не хранит,
        // а человек прислал его тем же клиентом минуту назад. Не было хода вовсе —
        // остаётся UTC, как у неизвестной зоны (раздел 10)
        var previous = await context.AssistantRuns
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(x => new { x.TimeZone, x.Language })
            .FirstOrDefaultAsync(ct);

        var run = Start(userId, workspaceId, previous?.TimeZone ?? "UTC",
            previous?.Language ?? LanguageConstants.Default, DateTime.UtcNow);
        waiting.RunId = run.Id;

        await context.AssistantRuns.AddAsync(run, ct);
        context.Enqueue(new AssistantRunCommand { RunId = run.Id });

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsDuplicate(e, DbErrors.AssistantRunningIndex))
        {
            // Ход треду уже завёл кто-то другой — ждущее сообщение подхватит его конец
            context.ChangeTracker.Clear();

            return null;
        }

        return run.Id;
    }

    /// <summary>
    /// «Остановить». Статус — гарантия, отмена токена — быстрота (раздел 3).
    /// Чужой прогон отвечает так же, как несуществующий (раздел 14)
    /// </summary>
    public async Task<bool> Stop(Guid runId, Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var run = await context.AssistantRuns
            .SingleOrDefaultAsync(x => x.Id == runId && x.UserId == userId && x.WorkspaceId == workspaceId, ct);
        if (run == null) return false;

        if (run.Status == AssistantRunStatus.Running)
        {
            run.Status = AssistantRunStatus.Stopped;
            run.StopReason = AssistantStopReason.UserStopped;
            await context.SaveChangesAsync(ct);
        }

        cancellation.Cancel(runId);

        return true;
    }

    /// <summary>
    /// Страница ленты (раздел 14). Первый запрос отдаёт последние
    /// <c>HistoryPageSize</c> сообщений, прокрутка вверх — следующие по курсору.
    /// Сортировка и курсор — по <c>Id</c>: он монотонен (12.2), и <c>uuid</c> Postgres
    /// сравнивает байты в том же порядке, в каком растёт время версии 7
    /// </summary>
    public async Task<AssistantHistoryDto> History(
        Guid userId,
        Guid workspaceId,
        Guid? cursor,
        CancellationToken ct)
    {
        var size = options.Value.HistoryPageSize;

        // Резюме компакции — служебное сообщение: человек видит в ленте свою
        // переписку целиком, а резюме существует только для модели (12.4)
        var thread = context.AssistantMessages
            .AsNoTracking()
            .Where(x => x.UserId == userId
                        && x.WorkspaceId == workspaceId
                        && x.Role != AssistantMessageRole.Summary);

        if (cursor is { } before) thread = thread.Where(x => x.Id.CompareTo(before) < 0);

        var page = await thread
            .OrderByDescending(x => x.Id)
            .Take(size)
            .ToArrayAsync(ct);

        var messages = page.Reverse().ToArray();

        var planIds = messages.Where(x => x.PlanId.HasValue).Select(x => x.PlanId!.Value).Distinct().ToArray();
        var plans = await context.AssistantPlans
            .AsNoTracking()
            .Where(x => planIds.Contains(x.Id))
            .ToArrayAsync(ct);

        // Один план едет с двумя сообщениями: с тем, где его предложили, и с тем, где
        // отчитались о применении (5.1). Состояние у плана при этом одно, и без этой
        // пометки отчёт — «Готово» с галочками и ссылками — дорисовывался задним числом
        // и к предложению: в ленте выходили два одинаковых блока. Отчёт принадлежит
        // сообщениям после предложения, в самом предложении план остаётся таким,
        // каким его прочитали
        var proposals = await Proposals(planIds, ct);

        var running = await Running(userId, workspaceId, ct);

        // Строка «Запомнил: …» в ленте живёт вместе с сообщением, а сам факт можно
        // убрать в любой момент — поэтому в сообщении лежат идентификаторы,
        // а текст берётся из памяти на каждое чтение (12.5)
        var facts = await memory.ByIds([.. messages.SelectMany(x => Ids(x.FactIds)).Distinct()], ct);

        return new AssistantHistoryDto
        {
            Messages =
            [
                .. messages.Select(x => Message(
                    x,
                    plans.SingleOrDefault(p => p.Id == x.PlanId),
                    facts,
                    x.PlanId.HasValue && proposals.TryGetValue(x.PlanId.Value, out var shown) && shown == x.Id))
            ],
            // Остановленный, но ещё не дописанный ход клиенту уже не «идёт»: плашки у него нет
            ActiveRun = running is { Status: AssistantRunStatus.Running } ? Run(running, null) : null,
            // Страница набралась целиком — значит, выше, скорее всего, есть ещё
            NextCursor = messages.Length == size ? messages[0].Id : null
        };
    }

    /// <summary>
    /// Состояние хода. Чужой прогон — <c>null</c>, как несуществующий: «нет»
    /// и «не твоё» клиент различать не должен (раздел 14)
    /// </summary>
    public async Task<AssistantRunDto?> RunState(Guid runId, Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var run = await context.AssistantRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == runId && x.UserId == userId && x.WorkspaceId == workspaceId, ct);
        if (run == null) return null;

        var messageId = await context.AssistantMessages
            .Where(x => x.RunId == runId && x.Role == AssistantMessageRole.Assistant)
            .OrderByDescending(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

        return Run(run, messageId);
    }

    /// <summary>
    /// «Показать ход мысли» (13.2). Чужой прогон — <c>null</c>, как несуществующий.
    /// Прогон старше срока хранения трассы отвечает «подробности уже не хранятся» —
    /// не пустотой и не ошибкой (12.3)
    /// </summary>
    public async Task<AssistantTraceDto?> Trace(Guid runId, Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var started = await context.AssistantRuns
            .Where(x => x.Id == runId && x.UserId == userId && x.WorkspaceId == workspaceId)
            .Select(x => (DateTime?)x.StartedAtUtc)
            .SingleOrDefaultAsync(ct);
        if (started == null) return null;

        var steps = await context.AssistantTraceSteps
            .AsNoTracking()
            .OfRun(runId)
            .ToArrayAsync(ct);

        return new AssistantTraceDto
        {
            // Пусто и старше срока — вычистили; пусто и свежий — шагов не было
            // (ходить было некуда) или ход только начался
            Expired = steps.Length == 0
                      && started < DateTime.UtcNow.AddDays(-options.Value.TraceRetentionDays),
            Steps =
            [
                .. steps.Select(x => new AssistantTraceStepDto
                {
                    Step = x.Step,
                    Kind = x.Kind,
                    ToolName = x.ToolName,
                    Summary = trace.Build(x.Kind, x.ToolName, x.Input),
                    DurationMs = x.DurationMs,
                    Error = x.Error
                })
            ]
        };
    }

    /// <summary>
    /// «Очистить» (12.1.1): стирает переписку и трассы. Прогоны и планы остаются — на прогонах
    /// держатся идемпотентность, метрики и учёт расхода, а план переживает очистку нарочно.
    /// Память не трогает (12.5): тред — переписка, память — то немногое, что из неё
    /// вынесли и оставили насовсем, и убирают её на своём экране
    /// </summary>
    public async Task Clear(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        // Ход, который ещё идёт или дописывает конец после «Остановить», допишет его уже
        // после очистки: пометка — до стирания, и по ней он не положит в пустую ленту
        // ни сообщения, ни плана, а свою трассу унесёт сам (AssistantRunService.Finish)
        var running = await Running(userId, workspaceId, ct);
        if (running != null)
        {
            await context.AssistantRuns
                .Where(x => x.Id == running.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.Cleared, true), ct);
            await Stop(running.Id, userId, workspaceId, ct);
        }

        // Трасса — та же переписка в служебном виде: что ушло в модель, дословно (13.1)
        var runs = context.AssistantRuns
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .Select(x => x.Id);
        await context.AssistantTraceSteps
            .Where(x => runs.Contains(x.RunId))
            .ExecuteDeleteAsync(ct);

        await context.AssistantMessages
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .ExecuteDeleteAsync(ct);

        // Показанный план после очистки ждать нечего: сообщения с ним больше нет
        await context.AssistantPlans
            .Where(x => x.UserId == userId
                        && x.WorkspaceId == workspaceId
                        && (x.Status == AssistantPlanStatus.Pending || x.Status == AssistantPlanStatus.Draft))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AssistantPlanStatus.Cancelled), ct);
    }

    private AssistantRunDto Run(AssistantRun run, Guid? messageId) => new()
    {
        Id = run.Id,
        Status = run.Status,
        Step = run.Step,
        StepsHard = run.Escalated ? options.Value.StepsHard : options.Value.StepsSoft,
        Escalated = run.Escalated,
        CurrentActivity = run.CurrentActivity,
        StopReason = run.StopReason,
        FinishedAtUtc = run.FinishedAtUtc,
        MessageId = messageId
    };

    /// <summary>
    /// Реплика ждёт своей очереди в треде: прогона у неё нет, пока идёт чужой
    /// (раздел 3). Вмешательства в идущий ход нет — у сообщения нет адресата
    /// </summary>
    private async Task<Guid> Wait(AssistantMessage message, CancellationToken ct)
    {
        await context.AssistantMessages.AddAsync(message, ct);
        await context.SaveChangesAsync(ct);

        return message.Id;
    }

    /// <summary>
    /// Сообщение, в котором план показали впервые, — по плану на страницу ленты.
    /// Считается по всему треду, а не по странице: предложение и отчёт запросто
    /// расходятся по разным страницам прокрутки
    /// </summary>
    private async Task<Dictionary<Guid, Guid>> Proposals(Guid[] planIds, CancellationToken ct)
    {
        if (planIds.Length == 0) return [];

        var shown = await context.AssistantMessages
            .Where(x => x.PlanId.HasValue && planIds.Contains(x.PlanId.Value))
            .Select(x => new { x.Id, PlanId = x.PlanId!.Value })
            .ToArrayAsync(ct);

        return shown
            .GroupBy(x => x.PlanId)
            .ToDictionary(group => group.Key, group => group.Min(x => x.Id));
    }

    private static AssistantMessageDto Message(
        AssistantMessage message,
        AssistantPlan? plan,
        IReadOnlyList<AssistantFact> facts,
        bool proposal) => new()
    {
        Id = message.Id,
        Role = message.Role,
        Content = message.Content,
        RunId = message.RunId,
        Options = string.IsNullOrEmpty(message.Options)
            ? null
            : JsonSerializer.Deserialize<string[]>(message.Options),
        Plan = plan == null
            ? null
            : new AssistantPlanDto
            {
                Id = plan.Id,
                // «Применён» — про отчёт, а не про предложение. Отменённый план в своём
                // сообщении остаётся отменённым: человек видит, что предлагалось
                // и что это не сделано (5.1)
                Status = proposal && plan.Status == AssistantPlanStatus.Applied
                    ? AssistantPlanStatus.Pending
                    : plan.Status,
                Operations = PlanJson.Deserialize<PlanDocument>(plan.Operations) ?? new PlanDocument(),
                Result = proposal || string.IsNullOrWhiteSpace(plan.Result)
                    ? null
                    : PlanJson.Deserialize<PlanResult>(plan.Result),
                AppliedAtUtc = proposal ? null : plan.AppliedAtUtc
            },
        // Удалённый факт из строки пропадает, а сообщение остаётся — человек видит
        // свою переписку целиком
        Facts =
        [
            .. Ids(message.FactIds)
                .Select(id => facts.SingleOrDefault(x => x.Id == id))
                .OfType<AssistantFact>()
                .Select(MemoryMapping.ToDto)
        ],
        CreatedAtUtc = message.CreatedAtUtc
    };

    private static Guid[] Ids(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<Guid[]>(json) ?? [];

    /// <summary>
    /// Логины участников одной строкой — подсказка распознавателю (6.2). Тот же список,
    /// что едет в системный промпт: имена, которых распознаватель не знает, иначе
    /// превращаются в случайные слова
    /// </summary>
    public async Task<string> MemberNames(Guid workspaceId, CancellationToken ct)
    {
        var memberIds = context.UserInPermissions
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => x.UserId);

        var names = await context.Profiles
            .Where(x => memberIds.Contains(x.Id))
            .OrderBy(x => x.UserName)
            .Select(x => x.UserName)
            .ToArrayAsync(ct);

        return string.Join(", ", names);
    }

    /// <summary>
    /// Живой ход треда — не законченный (<c>FinishedAtUtc</c> пуст), а не только <c>Running</c>:
    /// остановленный ход ещё дописывает свой конец, и новая реплика ждёт его, а не заводит
    /// второй ход рядом (раздел 3). Ровно это держит и индекс <c>ix_assistant_runs_running</c>
    /// </summary>
    private async Task<AssistantRun?> Running(Guid userId, Guid workspaceId, CancellationToken ct) =>
        await context.AssistantRuns
            .Where(x => x.UserId == userId
                        && x.WorkspaceId == workspaceId
                        && x.FinishedAtUtc == null)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

    private static AssistantRun Start(Guid userId, Guid workspaceId, string timeZone, string language, DateTime now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            WorkspaceId = workspaceId,
            TimeZone = timeZone,
            Language = language,
            Status = AssistantRunStatus.Running,
            StartedAtUtc = now
        };
}
