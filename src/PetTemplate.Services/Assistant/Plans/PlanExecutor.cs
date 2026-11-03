using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Services.Assistant.Plans;

/// <summary>
/// Единственное место, которое пишет в базу по воле ассистента (спец. assistant, 5.1).
///
/// Пишущие инструменты разделов ничего не меняют — они складывают задуманное
/// в <see cref="PlanCollector"/>. Пишет только <c>apply_plan</c>, и только то, что
/// человек прочитал: исполняется сохранённый JSON слово в слово, а не то, что модель
/// сочинит в момент согласия. Саму запись делает обработчик операции раздела
/// (<see cref="IPlanOperationHandler"/>); здесь — порядок, ссылки, права, сверка
/// и идемпотентность, общие для всех разделов.
/// </summary>
public class PlanExecutor(
    ApplicationContext context,
    PlanOperations registry,
    AssistantPermissions permissions,
    AssistantContext runContext)
{
    /// <summary>
    /// Хвост ленты, которого хватает страховке «реплика сразу за планом»: план
    /// и ответ на него идут подряд
    /// </summary>
    private const int TailSize = 10;

    /// <summary>
    /// Применяет сохранённый план. «Сейчас» и зона, с которыми сверяются даты плана
    /// (раздел 10), — в контексте хода: план, показанный в 23:55 и одобренный в 00:05,
    /// уже содержит «сегодня», которое стало вчера
    /// </summary>
    public async Task<ApplyOutcome> Apply(Guid planId, Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var plan = await context.AssistantPlans.SingleOrDefaultAsync(x => x.Id == planId, ct);

        // Чужой план отвечает так же, как несуществующий: «нет» и «не твоё»
        // клиент различать не должен (раздел 14)
        if (plan == null || plan.UserId != userId || plan.WorkspaceId != workspaceId)
            return new ApplyOutcome { Kind = ApplyOutcomeKind.NotFound };

        // Второе «да» вторых записей не создаёт, а отвечает тем, что уже сделано (5.1)
        if (plan.Status == AssistantPlanStatus.Applied)
            return new ApplyOutcome { Kind = ApplyOutcomeKind.AlreadyApplied, Result = ReadResult(plan) };

        if (plan.Status == AssistantPlanStatus.Cancelled)
            return new ApplyOutcome { Kind = ApplyOutcomeKind.Cancelled };

        var document = PlanJson.Deserialize<PlanDocument>(plan.Operations) ?? new PlanDocument();

        // Те же предохранители, что при сборке: JSON лежал в базе, и за это время
        // его могли поправить в обход копилки — или раздел с операцией убрали из сборки
        var revalidated = Revalidate(document);
        if (revalidated != null)
            return new ApplyOutcome { Kind = ApplyOutcomeKind.Mismatch, Mismatches = [revalidated] };

        var result = ReadResult(plan) ?? new PlanResult();

        // Сверяется только то, что ещё предстоит сделать. На повторе команды план
        // уже наполовину применён, и сверка по всему списку нашла бы «такая запись
        // уже есть» — ту самую, которую создала первая попытка
        var pending = document.Operations
            .Where(x => result.Operations.TrueForAll(y => y.Index != x.Index))
            .ToArray();

        var mismatches = await Check(pending, userId, workspaceId, ct);
        if (mismatches.Count > 0)
            return new ApplyOutcome { Kind = ApplyOutcomeKind.Mismatch, Mismatches = mismatches };

        foreach (var operation in document.Operations.OrderBy(x => x.Index))
        {
            ct.ThrowIfCancellationRequested();

            var step = await ApplyStep(plan, operation, userId, workspaceId, ct);

            // План закрыл другой применяющий, пока этот шёл по операциям: второй раз
            // ничего не делается, ответ — тот же, что у второго «да» (5.1)
            if (step == null) return Closed(plan);

            result = step;
        }

        // Закрывает план тот, кто застал его открытым под замком: у второго применяющего
        // план уже Applied, и «Готово» второй раз он не пишет
        await using (var transaction = await NestedTransaction.Begin(context.Database, ct))
        {
            if (await Lock(plan, ct) == null) return Closed(plan);

            plan.Status = AssistantPlanStatus.Applied;
            plan.AppliedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        return new ApplyOutcome { Kind = ApplyOutcomeKind.Applied, Result = result };
    }

    private static ApplyOutcome Closed(AssistantPlan plan) => plan.Status == AssistantPlanStatus.Applied
        ? new ApplyOutcome { Kind = ApplyOutcomeKind.AlreadyApplied, Result = ReadResult(plan) }
        : new ApplyOutcome { Kind = ApplyOutcomeKind.Cancelled };

    /// <summary>
    /// Одна операция плана и её строка в <c>Result</c> — одной транзакцией (5.1): обработчик
    /// раздела пишет тем же контекстом, и запись операции без отметки о ней не переживёт
    /// падения. Иначе повтор команды после сбоя между ними создал бы запись второй раз.
    ///
    /// План в транзакции под замком строки и перечитан: два применяющих одного плана
    /// (повтор команды, пока первая ещё идёт) делают каждую операцию по одному разу —
    /// второй ждёт замка и видит её в результате. Закрытый за это время план — <c>null</c>.
    ///
    /// Отказ операции откатывает то, что обработчик успел записать, — сделанной она
    /// не значится, — а сам отказ записывается следующей транзакцией
    /// </summary>
    private async Task<PlanResult?> ApplyStep(
        AssistantPlan plan,
        PlanOperation operation,
        Guid userId,
        Guid workspaceId,
        CancellationToken ct)
    {
        PlanOperationResult? refused = null;
        for (var pass = 0; pass < 2; pass++)
        {
            await using var transaction = await NestedTransaction.Begin(context.Database, ct);

            var result = await Lock(plan, ct);
            if (result == null) return null;

            // Сделал другой применяющий, пока этот ждал замка
            if (result.Operations.Exists(x => x.Index == operation.Index))
            {
                await transaction.CommitAsync(ct);
                return result;
            }

            var tracked = context.ChangeTracker.Entries().Select(x => x.Entity).ToHashSet();
            var done = refused ?? await ApplyOne(operation, result, userId, workspaceId, ct);

            if (!done.Done && refused == null)
            {
                // Отказ: записанное обработчиком до отказа уходит вместе с транзакцией,
                // а из трекера — то, что он успел в него положить
                await transaction.RollbackAsync(ct);
                foreach (var entry in context.ChangeTracker.Entries().Where(x => !tracked.Contains(x.Entity)).ToArray())
                    entry.State = EntityState.Detached;

                refused = done;
                continue;
            }

            result.Operations.Add(done);
            plan.Result = PlanJson.Serialize(result);
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return result;
        }

        throw new InvalidOperationException("Plan operation is neither applied nor refused");
    }

    /// <summary>
    /// Замок строки плана до конца транзакции и её свежая копия. Открытого плана уже нет —
    /// <c>null</c>
    /// </summary>
    private async Task<PlanResult?> Lock(AssistantPlan plan, CancellationToken ct)
    {
        await context.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM assistant_plans WHERE id = {plan.Id} FOR UPDATE", ct);

        await context.Entry(plan).ReloadAsync(ct);

        if (plan.Status is not (AssistantPlanStatus.Pending or AssistantPlanStatus.Draft)) return null;

        return ReadResult(plan) ?? new PlanResult();
    }

    /// <summary>
    /// Сохраняет план, собранный <b>в этом же ходе</b>, чтобы тут же его и применить —
    /// прямое поручение, в котором ассистенту не пришлось ничего решать (5.1).
    ///
    /// Сам список никуда не девается — он приезжает отчётом «Готово» в том же сообщении.
    /// Что вообще можно применить так, решает <see cref="PlanConsent"/>, и решает
    /// до вызова: сюда план приходит уже разрешённым.
    /// </summary>
    public async Task<AssistantPlan> SaveForImmediateApply(
        IReadOnlyList<PlanOperation> operations,
        Guid userId,
        Guid workspaceId,
        Guid runId,
        CancellationToken ct)
    {
        await CancelOpen(userId, workspaceId, ct);

        var plan = new AssistantPlan
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            WorkspaceId = workspaceId,
            RunId = runId == Guid.Empty ? null : runId,
            Status = AssistantPlanStatus.Pending,
            Operations = PlanJson.Serialize(new PlanDocument { Operations = [.. operations] }),
            CreatedAtUtc = DateTime.UtcNow
        };
        await context.AssistantPlans.AddAsync(plan, ct);
        await context.SaveChangesAsync(ct);

        return plan;
    }

    /// <summary>
    /// Незакрытый план в треде один (12.2.1): показанный раньше закрывается новым.
    /// Сохраняет вызывающий — тем же сохранением, что и новый план
    /// </summary>
    public async Task CancelOpen(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var open = await context.AssistantPlans
            .Where(x => x.UserId == userId
                        && x.WorkspaceId == workspaceId
                        && (x.Status == AssistantPlanStatus.Pending || x.Status == AssistantPlanStatus.Draft))
            .ToArrayAsync(ct);
        foreach (var previous in open) previous.Status = AssistantPlanStatus.Cancelled;
    }

    /// <summary>
    /// Показанный человеку план, который ждёт «да» (5.1). Незакрытый план в треде
    /// один (12.2.1), поэтому и берётся он однозначно
    /// </summary>
    public async Task<AssistantPlan?> PendingPlan(Guid userId, Guid workspaceId, CancellationToken ct) =>
        await context.AssistantPlans
            .Where(x => x.UserId == userId
                        && x.WorkspaceId == workspaceId
                        && x.Status == AssistantPlanStatus.Pending)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Единственная детерминированная страховка применения (5.1): план применяется,
    /// только если последняя реплика человека идёт <b>непосредственно</b> за сообщением,
    /// в котором план показан.
    ///
    /// Реплика через две — это уже разговор о другом, и «да» в ней относится неизвестно
    /// к чему. Что модель позвала <c>apply_plan</c> на «хм» или на «а сколько это стоит?»,
    /// страховка не ловит — на это есть метрика «применений, после которых попросили
    /// откат» (13.4; появится с первым откатом раздела, 7.1).
    ///
    /// Порядок в треде задаёт <c>Id</c> (<c>Guid</c> версии 7 монотонен, 12.2); берём
    /// хвост ленты и упорядочиваем в памяти — план, показанный раньше хвоста, страховку
    /// и так не проходит
    /// </summary>
    public async Task<bool> IsAnswerToPlan(Guid planId, Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var tail = await context.AssistantMessages
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(TailSize)
            .Select(x => new { x.Id, x.Role, x.PlanId })
            .ToArrayAsync(ct);

        var ordered = tail.OrderBy(x => x.Id).ToArray();
        if (ordered.Length < 2) return false;

        var last = ordered[^1];
        var beforeLast = ordered[^2];

        return last.Role == AssistantMessageRole.User && beforeLast.PlanId == planId;
    }

    /// <summary>
    /// Отмена плана: отказ, смена темы, остановка хода или «да» не к месту (5.4)
    /// </summary>
    public async Task Cancel(Guid planId, CancellationToken ct)
    {
        var plan = await context.AssistantPlans.SingleOrDefaultAsync(x => x.Id == planId, ct);
        if (plan == null) return;

        plan.Status = AssistantPlanStatus.Cancelled;
        await context.SaveChangesAsync(ct);
    }

    private static PlanResult? ReadResult(AssistantPlan plan) =>
        string.IsNullOrWhiteSpace(plan.Result) ? null : PlanJson.Deserialize<PlanResult>(plan.Result);

    /// <summary>
    /// Прогоняет план через ту же копилку, что собирала его: потолок в двадцать дел,
    /// свои потолки операций, ссылки только назад и только на свою рамку (раздел 8)
    /// </summary>
    private string? Revalidate(PlanDocument document)
    {
        var collector = new PlanCollector(registry);
        try
        {
            foreach (var operation in document.Operations.OrderBy(x => x.Index))
                collector.Add(operation.Kind, operation.Args);
        }
        catch (PlanRejectedException e)
        {
            return e.Message;
        }

        return null;
    }

    /// <summary>
    /// Сверка с базой перед применением (5.4) — всегда, а не только у состарившегося
    /// плана. Список закрытый: на месте ли автор просьбы и что скажет сверка каждой
    /// операции. Расхождение — не применяется <b>ничего</b>
    /// </summary>
    private async Task<List<string>> Check(
        IReadOnlyList<PlanOperation> pending,
        Guid userId,
        Guid workspaceId,
        CancellationToken ct)
    {
        var mismatches = new List<string>();
        if (pending.Count == 0) return mismatches;

        // Пока человек думал, его самого могли убрать из пространства
        if (!await permissions.IsMember(userId, workspaceId, ct))
        {
            mismatches.Add("автора просьбы больше нет в этом пространстве");

            return mismatches;
        }

        foreach (var operation in pending.OrderBy(x => x.Index))
        {
            // Ссылки на ещё не созданное сверять не с чем: рамка этого же плана
            // появится по ходу применения
            if (HasPendingLinks(operation)) continue;

            var mismatch = await registry.Find(operation.Kind)!.Check(operation.Args, runContext, ct);
            if (mismatch != null) mismatches.Add($"№{operation.Index}: {mismatch}");
        }

        return mismatches;
    }

    private async Task<PlanOperationResult> ApplyOne(
        PlanOperation operation,
        PlanResult result,
        Guid userId,
        Guid workspaceId,
        CancellationToken ct)
    {
        var handler = registry.Find(operation.Kind)!;

        // Рамка не создалась — класть запись «куда-нибудь» нельзя: она потеряется
        // так же, как без записи
        if (!TryResolve(operation.Args, result, out var args, out var broken))
            return Refused(operation, $"не выполнена операция №{broken}, от которой эта зависит");

        // Право отбирают и между показом и «да» (5.4): частичный отказ показывается
        // тем же списком с крестиками, что и отказ сервиса
        if (!await permissions.Has(userId, workspaceId, ct, [.. handler.Permissions]))
            return Refused(operation, "нет права на это в этом пространстве");

        try
        {
            var createdId = await handler.Apply(args, runContext, ct);

            return new PlanOperationResult { Index = operation.Index, Done = true, CreatedId = createdId };
        }
        // Отказ сервиса — результат операции, а не исключение наверх: частичный отказ
        // показывается человеку тем же списком, с крестиком и причиной (5.1)
        catch (Exception e) when (e is NotFoundException or BadRequestException or ConflictException)
        {
            return Refused(operation, e.Message);
        }
    }

    private static PlanOperationResult Refused(PlanOperation operation, string error) =>
        new() { Index = operation.Index, Done = false, Error = error };

    private static bool HasPendingLinks(PlanOperation operation) =>
        PlanCollector.Links(operation.Args).Any(x => PlanCollector.TargetOf(x) != null);

    /// <summary>
    /// Подставить вместо <c>"#n"</c> идентификатор, созданный операцией <c>n</c>.
    /// Ссылка только назад (раздел 8), поэтому к этому моменту результат у неё есть —
    /// или отказ, и тогда эта операция не исполняется
    /// </summary>
    private static bool TryResolve(JsonElement args, PlanResult result, out JsonElement resolved, out int broken)
    {
        broken = 0;
        resolved = args;
        if (!PlanCollector.Links(args).Any(x => PlanCollector.TargetOf(x) != null)) return true;

        var node = JsonNode.Parse(args.GetRawText());
        var failed = 0;
        Replace(node, result, ref failed);
        if (failed != 0)
        {
            broken = failed;
            return false;
        }

        resolved = JsonSerializer.SerializeToElement(node);

        return true;
    }

    private static void Replace(JsonNode? node, PlanResult result, ref int failed)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToArray())
                {
                    if (value is JsonValue leaf && PlanCollector.IsLink(name)
                        && leaf.TryGetValue<string>(out var text) && PlanCollector.TargetOf(text) is { } target)
                    {
                        var done = result.Operations.SingleOrDefault(x => x.Index == target);
                        if (done is not { Done: true })
                        {
                            failed = target;
                            return;
                        }

                        obj[name] = done.CreatedId;
                        continue;
                    }

                    Replace(value, result, ref failed);
                }

                break;
            case JsonArray array:
                foreach (var item in array) Replace(item, result, ref failed);
                break;
        }
    }
}
