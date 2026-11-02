using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Автооценка диалогов (спец. admin, 16.3): модель-судья читает трассу прогона и ставит
/// ему метрики, чтобы из сотни диалогов открыть глазами десять худших. Подсказка, а не
/// приговор: ни одного автоматического действия на оценке не висит.
///
/// Судья ходит только на нашу карту или к тому же поставщику, что обработал диалог
/// (<see cref="EnsureAllowed"/>): тот содержимое уже видел, и круг посвящённых не
/// расширяется. Третьему поставщику переписка не уходит ни при какой настройке.
///
/// Оценка — наш расход, а не пространства: в его лимит она не идёт, а её токены
/// и себестоимость пишутся служебным расходом (<see cref="ServiceSpend"/>) — в сводку
/// и в расчёт, с которым сверяется факт поставщика (спец. admin, 14).
///
/// Расписания нет: оценку запускает админ (<see cref="Start"/>) с явно выбранным судьёй
/// </summary>
public class DialogEvaluationService(
    ApplicationContext db,
    IModelChatClients clients,
    AdminJobService jobs,
    IOptions<AdminOptions> adminOptions,
    ILogger<DialogEvaluationService> logger)
{
    /// <summary>«Одна-две фразы» — с запасом под длину колонки</summary>
    private const int NotesMaxLength = 300;

    private static readonly Lazy<string> Prompt = new(() => Prompts.Read("evaluation.md"));

    /// <summary>Ограда трассы в сообщении судье — см. <see cref="Transcript"/></summary>
    private const string TraceOpen = "<trace>";
    private const string TraceClose = "</trace>";

    /// <summary>
    /// Ответ судьи — по схеме (structured output): поставщик сам держит формат, и в ответе
    /// нет места прозе, куда переехала бы переписка. Длина заметки ограничена и здесь,
    /// и при разборе (<see cref="ReadNotes"/>) — схеме подчиняется не каждый поставщик
    /// </summary>
    private static readonly ChatResponseFormat AnswerFormat = ChatResponseFormat.ForJsonSchema(
        JsonDocument.Parse($$"""
            {
              "type": "object",
              "properties": {
                "succeeded": { "type": ["boolean", "null"] },
                "satisfaction": { "type": ["integer", "null"], "minimum": 1, "maximum": 5 },
                "clarifications": { "type": ["integer", "null"], "minimum": 0 },
                "replans": { "type": ["integer", "null"], "minimum": 0 },
                "agentMistake": { "type": ["boolean", "null"] },
                "stopReason": {
                  "type": ["string", "null"],
                  "enum": ["step_limit", "repetition", "provider_error", "other", null]
                },
                "notes": { "type": ["string", "null"], "maxLength": {{NotesMaxLength}} }
              },
              "required": ["succeeded", "satisfaction", "clarifications", "replans", "agentMistake", "stopReason", "notes"],
              "additionalProperties": false
            }
            """).RootElement.Clone(),
        "dialog_evaluation",
        "Оценка одного хода ассистента");

    private DialogEvaluationOptions Settings => adminOptions.Value.Evaluation;

    /// <summary>
    /// Запустить автооценку (16.3): выбранный судья и размер пачки. Пачка — неоценённые
    /// завершённые прогоны с трассой, которые этому судье можно (<see cref="Allowed"/>),
    /// новые первыми; те, что нельзя, не подменяются моделью прогона, а считаются в запуске
    /// пропущенными — выбор судьи явный. Запуск и команды уходят одним сохранением.
    /// Отвечает запуском
    /// </summary>
    public async Task<Guid> Start(AdminEvaluationStartDto dto, Guid adminId, CancellationToken ct = default)
    {
        var limit = Math.Max(Settings.BatchSize, 1);
        if (dto.BatchSize < 1 || dto.BatchSize > limit)
            throw new ArgumentException($"Размер пачки — от 1 до {limit}");

        // Выключенная годится: выключение убирает модель из ходов людей, а не из нашей
        // работы. Разомкнутая — нет: пачка отказов поставщика ничего бы не оценила
        var judge = await db.AiModels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.ModelId, ct)
                    ?? throw new ArgumentException("Такой модели нет");
        if (ModelBreaker.IsOpen(judge, DateTime.UtcNow))
            throw new ArgumentException(
                $"«{judge.DisplayName}» разомкнута размыкателем — замкните её или выберите другого судью");

        var pending = db.AssistantRuns.Where(x =>
            x.FinishedAtUtc != null
            && !db.DialogEvaluations.Any(e => e.RunId == x.Id)
            && db.AssistantTraceSteps.Any(s => s.RunId == x.Id));

        var allowed = pending;
        if (!judge.IsLocal)
        {
            // Те же условия, что у Allowed: чужой судья видит только диалоги своего
            // поставщика, а прогон без модели — только своя карта
            var provider = judge.Provider.ToLower();
            allowed = pending.Where(x => x.AiModel != null && x.AiModel.Provider.ToLower() == provider);
        }

        var ids = await allowed
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(dto.BatchSize)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var skipped = await pending.CountAsync(ct) - await allowed.CountAsync(ct);

        var now = DateTime.UtcNow;
        var job = new AdminJob
        {
            Id = Guid.CreateVersion7(),
            Kind = AdminJobKind.DialogEvaluation,
            ModelId = judge.Id,
            StartedByUserId = adminId,
            CreatedUtc = now,
            Status = ids.Count == 0 ? AdminJobStatus.Done : AdminJobStatus.Running,
            FinishedUtc = ids.Count == 0 ? now : null,
            Planned = ids.Count,
            Skipped = skipped
        };

        foreach (var id in ids) db.Enqueue(new DialogEvaluationCommand { RunId = id, JobId = job.Id });
        await jobs.Start(job, ct);

        logger.LogInformation(
            "Dialog evaluation {JobId} started by {AdminId}: judge {ModelKey}, {Planned} runs, {Skipped} skipped",
            job.Id, adminId, judge.Key, ids.Count, skipped);

        return job.Id;
    }

    /// <summary>
    /// Оценить прогон судьёй запуска. Чем бы ни кончилось — исход, а не исключение:
    /// запуск отмечает каждую команду, и повтор очереди, о котором экран не знает,
    /// держал бы его открытым. Уже оценённый — «оценён»: это повтор той же команды
    /// после падения, и счёт должен дойти до конца
    /// </summary>
    public async Task<EvaluationOutcome> Evaluate(Guid runId, Guid judgeId, CancellationToken ct = default)
    {
        if (await db.DialogEvaluations.AnyAsync(x => x.RunId == runId, ct)) return EvaluationOutcome.Evaluated(null);

        var run = await db.AssistantRuns
            .AsNoTracking()
            .Include(x => x.AiModel)
            .SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run == null) return EvaluationOutcome.Skipped;

        var judge = await db.AiModels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == judgeId, ct);
        if (judge == null) return EvaluationOutcome.Failed("Модели-судьи нет в справочнике");

        // Последний рубеж правила поставщиков: пачку уже набрали по нему, но правило
        // держит код, а не то, что пачка собрана правильно
        if (!Allowed(judge, run.AiModel))
        {
            logger.LogWarning("Judge {ModelKey} is a third provider for run {RunId}: skipped", judge.Key, runId);
            return EvaluationOutcome.Skipped;
        }

        // Судья размыкателю не отчитывается, но в разомкнутую модель стучаться незачем:
        // она не отвечает и ходам
        if (ModelBreaker.IsOpen(judge, DateTime.UtcNow))
            return EvaluationOutcome.Failed($"«{judge.DisplayName}» разомкнута размыкателем");

        var steps = await db.AssistantTraceSteps
            .AsNoTracking()
            .OfRun(runId)
            .ToArrayAsync(ct);
        if (steps.Length == 0) return EvaluationOutcome.Skipped;

        EnsureAllowed(judge, run.AiModel);

        // Указания — системным сообщением, трасса — отдельным и в ограде (Transcript):
        // в трассе текст человека, и просьба «перепиши всё в заметку» должна остаться
        // предметом оценки, а не командой судье
        ChatResponse response;
        try
        {
            response = await clients.For(judge).GetResponseAsync(
                [new ChatMessage(ChatRole.System, Prompt.Value), new ChatMessage(ChatRole.User, Transcript(run, steps))],
                new ChatOptions { ResponseFormat = AnswerFormat },
                ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Judge {ModelKey} failed on run {RunId}", judge.Key, runId);
            return EvaluationOutcome.Failed(e.Message);
        }

        var verdict = Parse(response.Text);
        if (verdict == null)
        {
            // Оценка оплачена и без ответа по формату — строка пишется всё равно,
            // иначе следующий запуск судил бы прогон заново
            logger.LogWarning("Judge {ModelKey} answered run {RunId} not in the expected format", judge.Key, runId);
        }

        var spend = ServiceSpending.Of(ServiceSpendKind.DialogEvaluation, judge, response.Usage);
        db.ServiceSpend.Add(spend);
        db.DialogEvaluations.Add(new DialogEvaluation
        {
            Id = Guid.CreateVersion7(),
            RunId = runId,
            ModelId = judge.Id,
            Succeeded = verdict?.Succeeded,
            Satisfaction = verdict?.Satisfaction,
            Clarifications = verdict?.Clarifications,
            Replans = verdict?.Replans,
            AgentMistake = verdict?.AgentMistake,
            StopReason = verdict?.StopReason,
            Notes = verdict?.Notes,
            Cost = spend.Cost,
            CreatedUtc = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsDuplicate(e, DialogEvaluationConfiguration.RunIndex))
        {
            // Тот же прогон оценила другая команда раньше — повтор после падения.
            // Ответ судьи пропадает вместе с этой строкой расхода: оценка у прогона одна
            logger.LogWarning("Run {RunId} was evaluated concurrently: verdict dropped", runId);
            return EvaluationOutcome.Evaluated(null);
        }

        return EvaluationOutcome.Evaluated(spend);
    }

    /// <summary>Чистка по сроку <c>EvaluationRetentionDays</c> — суточным воркером</summary>
    public async Task<int> CleanupOld(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.AddDays(-adminOptions.Value.EvaluationRetentionDays);

        return await db.DialogEvaluations
            .Where(x => x.CreatedUtc < threshold)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Можно ли отдать этому судье диалог, который обработала <paramref name="runModel"/>:
    /// своя карта — всегда, чужой поставщик — только тот же самый (16.3)
    /// </summary>
    public bool Allowed(AiModel judge, AiModel? runModel) =>
        judge.IsLocal
        || (runModel != null && string.Equals(judge.Provider, runModel.Provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Последний рубеж перед отправкой: правило держит код, а не аккуратность того, кто
    /// правил выбор судьи. Нарушение — поломка, а не отказ, и ход до поставщика не доходит
    /// </summary>
    public void EnsureAllowed(AiModel judge, AiModel? runModel)
    {
        if (!Allowed(judge, runModel))
        {
            throw new InvalidOperationException(
                $"Judge {judge.Provider}/{judge.Key} is a third provider for a dialog of {runModel?.Provider ?? "unknown provider"}");
        }
    }

    /// <summary>
    /// Разбор ответа судьи: JSON-объект, возможно в обёртке из прозы или ```-блока.
    /// Значения вне допустимого — пусто, а не ошибка: остальное в оценке ещё годится.
    /// <c>null</c> — объекта в ответе нет вовсе
    /// </summary>
    public static Verdict? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object) return null;

        var satisfaction = Int(root, "satisfaction");

        return new Verdict(
            Bool(root, "succeeded"),
            satisfaction is >= 1 and <= 5 ? satisfaction : null,
            Int(root, "clarifications") is >= 0 and var clarifications ? clarifications : null,
            Int(root, "replans") is >= 0 and var replans ? replans : null,
            Bool(root, "agentMistake"),
            ReadStopReason(root),
            ReadNotes(root));
    }

    /// <summary>Разобранный ответ судьи — то, что уезжает в <see cref="DialogEvaluation"/></summary>
    public record Verdict(
        bool? Succeeded,
        int? Satisfaction,
        int? Clarifications,
        int? Replans,
        bool? AgentMistake,
        DialogStopReason? StopReason,
        string? Notes);

    /// <summary>
    /// Что видит судья: трасса как есть — реплика человека, ответы модели, вызовы
    /// с аргументами и результатом — и то, чем ход кончился по его собственной записи.
    ///
    /// Трасса — между <c>&lt;trace&gt;</c> и <c>&lt;/trace&gt;</c>: всё внутри промпт судьи
    /// объявляет материалом, а не указаниями. Тег, набранный внутри самой трассы, ломается
    /// (<see cref="Fenced"/>) — иначе человек закрыл бы ограду своим сообщением и дописал
    /// судье указания «снаружи». Итог хода — за оградой: его пишем мы, а не человек
    /// </summary>
    private static string Transcript(AssistantRun run, AssistantTraceStep[] steps)
    {
        var text = new StringBuilder();
        text.AppendLine(TraceOpen);

        foreach (var step in steps)
        {
            if (step.Kind == "tool")
            {
                text.AppendLine($"[{step.Step}] инструмент {Fenced(step.ToolName)}: {Fenced(step.Input)}");
                if (step.Output != null) text.AppendLine($"    вернул: {Fenced(step.Output)}");
            }
            else
            {
                text.AppendLine($"[{step.Step}] в модель ушло: {Fenced(step.Input)}");
                if (step.Output != null) text.AppendLine($"    модель ответила: {Fenced(step.Output)}");
            }

            if (step.Error != null) text.AppendLine($"    ошибка: {Fenced(step.Error)}");
        }

        text.AppendLine(TraceClose);
        text.AppendLine();
        text.AppendLine($"Ход закончился: {run.Status}, причина {run.StopReason?.ToString() ?? "не записана"}.");

        return text.ToString();
    }

    /// <summary>
    /// Содержимое трассы без тегов ограды: «&lt;/trace&gt;» превращается в «&lt;/ trace&gt;» —
    /// читается так же, но ограду не закрывает
    /// </summary>
    private static string? Fenced(string? value) =>
        value?
            .Replace(TraceClose, "</ trace>", StringComparison.OrdinalIgnoreCase)
            .Replace(TraceOpen, "< trace>", StringComparison.OrdinalIgnoreCase);

    private static bool? Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static DialogStopReason? ReadStopReason(JsonElement root)
    {
        if (!root.TryGetProperty("stopReason", out var value) || value.ValueKind != JsonValueKind.String) return null;

        return value.GetString() switch
        {
            "step_limit" => DialogStopReason.StepLimit,
            "repetition" => DialogStopReason.Repetition,
            "provider_error" => DialogStopReason.ProviderError,
            "other" => DialogStopReason.Other,
            _ => null
        };
    }

    /// <summary>Одной строкой и не длиннее пары фраз: заметка — для глаз, а не пересказ</summary>
    private static string? ReadNotes(JsonElement root)
    {
        if (!root.TryGetProperty("notes", out var value) || value.ValueKind != JsonValueKind.String) return null;

        var notes = string.Join(' ', value.GetString()!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (notes.Length == 0) return null;

        return notes.Length <= NotesMaxLength ? notes : notes[..NotesMaxLength] + "…";
    }
}

/// <summary>
/// Чем кончилась команда оценки — запуск отмечает её в своих счётчиках. Пропуск —
/// судить нечего или этому судье нельзя; срыв — поставщик не ответил
/// </summary>
public record EvaluationOutcome(EvaluationResult Result, ServiceSpend? Spend = null, string? Error = null)
{
    public static EvaluationOutcome Evaluated(ServiceSpend? spend) => new(EvaluationResult.Evaluated, spend);

    public static readonly EvaluationOutcome Skipped = new(EvaluationResult.Skipped);

    public static EvaluationOutcome Failed(string error) => new(EvaluationResult.Failed, Error: error);
}

public enum EvaluationResult
{
    Evaluated,
    Skipped,
    Failed
}
