using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Hooks;

namespace PetTemplate.Services.Billing;

/// <summary>
/// Учёт хода (спец. assistant, 13.5): одна строка расхода на прогон, в момент
/// его завершения, по фактическим токенам и цене модели из справочника.
///
/// Без списания: в шаблоне
/// баланса нет — стоимость записывается, деньги ни у кого не снимаются. По этим
/// строкам считается лимит пространства (13.6) и сводка «Расход» в админке.
///
/// Зовётся внутри транзакции финального статуса прогона: ход закончился и не учтён,
/// или учтён и не закончился, — обе половины хуже, чем ни одной. Сохраняет вызывающий
/// </summary>
public class RunBilling(
    ApplicationContext db,
    BillingMetrics metrics,
    ILogger<RunBilling> logger) : IRunBilling
{
    /// <summary>
    /// Надо ли учитывать этот ход. Критерий один — токены: ход с нулевыми токенами
    /// (все модели разомкнуты, отказ на старте, упавший до ответа) строки не создаёт вовсе
    /// </summary>
    public bool ShouldCharge(AssistantRun run) => run.TokensIn + run.TokensOut > 0;

    /// <summary>
    /// Записать расход хода. Повтор по тому же прогону ничего не добавляет —
    /// строка на прогон одна (уникальный индекс <c>run_id</c>)
    /// </summary>
    public async Task Charge(AssistantRun run, CancellationToken ct)
    {
        if (!ShouldCharge(run)) return;

        var model = run.AiModelId is { } modelId
            ? await db.AiModels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == modelId, ct)
            : null;
        if (model == null)
        {
            // Модель пишется до первого обращения к ней (12.3): токены без модели —
            // поломка, и молчать о ней нельзя
            logger.LogError("Run {RunId} has tokens, but no model: spend is not recorded", run.Id);

            return;
        }

        if (await db.RunSpend.AnyAsync(x => x.RunId == run.Id, ct)) return;

        var cost = Cost(run.TokensIn, run.TokensCached, run.TokensOut, model);

        await db.RunSpend.AddAsync(new RunSpend
        {
            Id = Guid.CreateVersion7(),
            RunId = run.Id,
            WorkspaceId = run.WorkspaceId,
            UserId = run.UserId,
            ModelId = model.Id,
            TokensIn = run.TokensIn,
            TokensCached = run.TokensCached,
            TokensOut = run.TokensOut,
            Cost = cost,
            CreatedUtc = DateTime.UtcNow
        }, ct);

        metrics.Cost(model.Key, BillingMetrics.RunKind, cost);
    }

    /// <summary>
    /// Стоимость по прайсу поставщика (13.5). Цены — за 1000 токенов. Кешированные
    /// выделены из входящих; чего поставщик не разметил, остаётся входящим и идёт
    /// по входящей ставке — ошибаться надо в сторону «дороже»: по этой цифре
    /// срабатывает лимит и сверяется счёт поставщика
    /// </summary>
    public static decimal Cost(long tokensIn, long tokensCached, long tokensOut, AiModel model)
    {
        var cached = Math.Min(tokensCached, tokensIn);
        var plain = tokensIn - cached;

        return (plain * model.CostIn
                + cached * model.CostCached
                + tokensOut * model.CostOut) / 1000m;
    }
}
