using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Разбор диалогов (спец. admin, 16): список прогонов по признакам и трасса одного.
/// Единственное место в системе, где видно чужую переписку, — поэтому каждое открытие
/// трассы пишется в журнал доступа (<see cref="AdminAccessLogService"/>) до ответа.
///
/// Показывается только то, что и так лежит в трассе (спец. assistant, 13.1): ничего
/// дополнительно не пишется и не хранится дольше <c>TraceRetentionDays</c>. Сам прогон
/// трассу переживает (12.3) и остаётся в списке, а открытие старого отвечает «содержимого
/// уже нет». Расшифровок голоса здесь нет: у них нет прогона (13.5), их видно в «Расходе»
/// </summary>
public class AdminDialogService(
    ApplicationContext db,
    AdminAccessLogService accessLog,
    IOptions<AssistantOptions> assistantOptions)
{
    /// <summary>Период по умолчанию — «за 7 дней»</summary>
    private const int DefaultDays = 7;

    private const int PageSize = 50;

    public async Task<AdminDialogPageDto> List(AdminDialogFilterDto filter, CancellationToken ct = default)
    {
        var last = filter.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var first = filter.From ?? last.AddDays(1 - DefaultDays);
        if (first > last) throw new ArgumentException("Начало периода позже его конца");

        var since = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var until = last.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var runs = db.AssistantRuns
            .AsNoTracking()
            .Where(x => x.StartedAtUtc >= since && x.StartedAtUtc < until);

        var workspace = filter.Workspace?.Trim();
        if (!string.IsNullOrEmpty(workspace))
        {
            // Часть названия — без учёта регистра: админ вводит её по памяти
            runs = Guid.TryParse(workspace, out var workspaceId)
                ? runs.Where(x => x.WorkspaceId == workspaceId)
                : runs.Where(x => EF.Functions.ILike(x.Workspace.Name, "%" + Like(workspace) + "%"));
        }

        if (filter.ModelId is { } modelId) runs = runs.Where(x => x.AiModelId == modelId);
        if (filter.Status is { } status) runs = runs.Where(x => x.Status == status);

        // Уточнения и оценку ставит автооценка (16.3): прогон без оценки под такие
        // фильтры не подходит
        if (filter.MinClarifications is { } minClarifications)
        {
            runs = runs.Where(x => db.DialogEvaluations
                .Any(e => e.RunId == x.Id && e.Clarifications >= minClarifications));
        }

        if (filter.MaxSatisfaction is { } maxSatisfaction)
        {
            runs = runs.Where(x => db.DialogEvaluations
                .Any(e => e.RunId == x.Id && e.Satisfaction <= maxSatisfaction));
        }

        // Курсор — пара (время, Id): прогоны, начатые в одну метку, иначе терялись
        // бы на границе страницы
        if (filter.Before is { } before)
        {
            var cursor = await db.AssistantRuns
                .Where(x => x.Id == before)
                .Select(x => new { x.StartedAtUtc, x.Id })
                .SingleOrDefaultAsync(ct);
            if (cursor != null)
            {
                runs = runs.Where(x => x.StartedAtUtc < cursor.StartedAtUtc
                                       || (x.StartedAtUtc == cursor.StartedAtUtc && x.Id.CompareTo(cursor.Id) < 0));
            }
        }

        var page = await Rows(runs
                .OrderByDescending(x => x.StartedAtUtc)
                .ThenByDescending(x => x.Id)
                .Take(PageSize))
            .ToArrayAsync(ct);

        return new AdminDialogPageDto
        {
            Dialogs = page,
            NextBefore = page.Length == PageSize ? page[^1].RunId : null
        };
    }

    /// <summary>
    /// Трасса прогона — тем же запросом, что «Показать ход мысли» (спец. assistant, 13.2),
    /// без фильтра по пользователю. Запись в журнал уходит раньше, чем шаги прочитаны:
    /// не записалось — ответа нет. Пишется и открытие вычищенной трассы: смотрящий об
    /// этом не знал, и попытка прочитать — тоже след. <c>null</c> — прогона нет
    /// </summary>
    public async Task<AdminDialogTraceDto?> Open(Guid runId, Guid adminId, CancellationToken ct = default)
    {
        var dialog = await Rows(db.AssistantRuns.AsNoTracking().Where(x => x.Id == runId)).SingleOrDefaultAsync(ct);
        if (dialog == null) return null;

        await accessLog.Write(adminId, dialog.RunId, dialog.WorkspaceId, ct);

        var steps = await db.AssistantTraceSteps
            .AsNoTracking()
            .OfRun(runId)
            .ToArrayAsync(ct);

        var evaluation = await db.DialogEvaluations
            .AsNoTracking()
            .Where(x => x.RunId == runId)
            .Select(x => new AdminDialogEvaluationDto
            {
                ModelName = x.Model.DisplayName,
                Succeeded = x.Succeeded,
                Satisfaction = x.Satisfaction,
                Clarifications = x.Clarifications,
                Replans = x.Replans,
                AgentMistake = x.AgentMistake,
                StopReason = x.StopReason,
                Notes = x.Notes,
                CreatedUtc = x.CreatedUtc
            })
            .SingleOrDefaultAsync(ct);

        return new AdminDialogTraceDto
        {
            Dialog = dialog,
            Expired = dialog.TraceExpired,
            Evaluation = evaluation,
            Steps =
            [
                .. steps.Select(x => new AdminDialogStepDto
                {
                    Step = x.Step,
                    Kind = x.Kind,
                    ToolName = x.ToolName,
                    Summary = TraceSummary.Build(x.Kind, x.ToolName, x.Input),
                    Input = x.Input,
                    Output = x.Output,
                    DurationMs = x.DurationMs,
                    TokensIn = x.TokensIn,
                    TokensOut = x.TokensOut,
                    Error = x.Error,
                    CreatedUtc = x.CreatedAtUtc
                })
            ]
        };
    }

    /// <summary>
    /// Строка списка. Трасса считается вычищенной, когда прогон старше срока и шагов
    /// у него не осталось: у свежего прогона без шагов (упал до первого обращения)
    /// содержимого не было, а не «уже нет»
    /// </summary>
    private IQueryable<AdminDialogDto> Rows(IQueryable<AssistantRun> runs)
    {
        var threshold = DateTime.UtcNow.AddDays(-assistantOptions.Value.TraceRetentionDays);

        return runs.Select(x => new AdminDialogDto
        {
            RunId = x.Id,
            StartedUtc = x.StartedAtUtc,
            FinishedUtc = x.FinishedAtUtc,
            WorkspaceId = x.WorkspaceId,
            WorkspaceName = x.Workspace.Name,
            ModelId = x.AiModelId,
            ModelName = x.AiModel != null ? x.AiModel.DisplayName : null,
            Status = x.Status,
            StopReason = x.StopReason,
            Steps = x.Step,
            TokensIn = x.TokensIn,
            TokensOut = x.TokensOut,
            // Одна строка расхода на прогон — уникальный индекс run_id (спец. assistant, 13.5)
            Cost = db.RunSpend
                .Where(s => s.RunId == x.Id)
                .Select(s => s.Cost)
                .FirstOrDefault(),
            // Одна оценка на прогон — уникальный индекс run_id
            Clarifications = db.DialogEvaluations
                .Where(e => e.RunId == x.Id)
                .Select(e => e.Clarifications)
                .FirstOrDefault(),
            Satisfaction = db.DialogEvaluations
                .Where(e => e.RunId == x.Id)
                .Select(e => e.Satisfaction)
                .FirstOrDefault(),
            TraceExpired = x.StartedAtUtc < threshold && !db.AssistantTraceSteps.Any(s => s.RunId == x.Id)
        });
    }

    /// <summary>Подстрока для <c>ILIKE</c>: знаки шаблона во вводе — буквами, а не шаблоном</summary>
    private static string Like(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
