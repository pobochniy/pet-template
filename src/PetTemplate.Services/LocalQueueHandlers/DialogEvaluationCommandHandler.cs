using LocalQueue.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Admin;
using PetTemplate.Services.Commands;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Автооценка одного прогона (спец. admin, 16.3) — см. <see cref="DialogEvaluationService"/>.
/// Исход — оценён, пропущен, сорвался — отмечается в запуске, и последняя команда его
/// закрывает. Отказ поставщика в повтор очереди не уходит: повторит его следующий запуск,
/// а повтор, о котором экран не знает, держал бы запуск открытым.
///
/// Воркер — админский (<c>AddCommandQueue</c> в API): пачка оценок не задерживает ходы людей
/// </summary>
public class DialogEvaluationCommandHandler(
    ApplicationContext db,
    DialogEvaluationService evaluations,
    AdminJobService jobs,
    ILogger<DialogEvaluationCommandHandler> logger) : ICommandHandler<DialogEvaluationCommand>
{
    public async Task Handle(DialogEvaluationCommand command, CancellationToken ct)
    {
        var judge = await db.AdminJobs
            .Where(x => x.Id == command.JobId)
            .Select(x => (Guid?)x.ModelId)
            .SingleOrDefaultAsync(ct);
        if (judge == null)
        {
            logger.LogInformation("Dialog evaluation of run {RunId} has no job: dropped", command.RunId);
            return;
        }

        var outcome = await evaluations.Evaluate(command.RunId, judge.Value, ct);

        await jobs.Advance(
            command.JobId,
            outcome.Result == EvaluationResult.Evaluated ? 1 : 0,
            outcome.Result == EvaluationResult.Skipped ? 1 : 0,
            outcome.Result == EvaluationResult.Failed ? 1 : 0,
            outcome.Spend,
            outcome.Error,
            ct);
    }
}
