using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Billing;

/// <summary>
/// Периодическая проверка денег (спец. assistant, 13.5): снимок для приборов
/// <see cref="BillingMetrics"/> и предупреждение в лог. Тревоги по снимку —
/// в <c>IaC/grafana/provisioning/alerting/rules.yml</c>.
///
/// Остаток у поставщика ниже порога — предупреждение: кончится он — ассистент встанет
/// у всех разом. Расхождение расчёта с фактом — прибор: правильно ли мы считаем, а значит,
/// и лимиты пространств. Кошельков, курсов и платежей людей
/// в шаблоне нет
/// </summary>
public class BillingMonitor(
    ApplicationContext db,
    ProviderLedger ledger,
    BillingMetrics metrics,
    IOptions<SpendOptions> options,
    ILogger<BillingMonitor> logger)
{
    public async Task<BillingSnapshot> Check(CancellationToken ct = default)
    {
        var snapshot = new BillingSnapshot(await Providers(ct));

        metrics.Snapshot(snapshot);

        return snapshot;
    }

    private async Task<IReadOnlyList<ProviderSnapshot>> Providers(CancellationToken ct)
    {
        var facts = await ledger.Facts(ct);
        var providers = await db.AiModels.Select(x => x.Provider).Distinct().ToListAsync(ct);
        var alarm = options.Value.ProviderBalanceAlarm;

        var result = new List<ProviderSnapshot>();
        foreach (var provider in providers)
        {
            var account = await ledger.Account(provider, facts, ct);
            decimal? remaining = account.HasPayments ? account.Remaining : null;
            var low = remaining < alarm;

            if (low)
            {
                logger.LogWarning(
                    "Provider {Provider} account is low: {Remaining} left, alarm below {Alarm}",
                    provider, remaining, alarm);
            }

            // Главный сигнал сводки — по последнему введённому периоду: старые
            // факты уже разобраны, а свежий разъезд виден только в новом
            var last = facts.Where(x => x.Provider == provider).MaxBy(x => x.PeriodTo);
            double? discrepancy = null;
            if (last is { Amount: > 0 })
            {
                var estimate = await ledger.Estimate(provider, last.PeriodFrom!.Value, last.PeriodTo, ct);
                discrepancy = (double)((estimate - last.Amount) / last.Amount);
            }

            result.Add(new ProviderSnapshot(provider, remaining, low, discrepancy));
        }

        return result;
    }
}
