using Microsoft.Extensions.Options;
using PetTemplate.Services.Billing;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Денежные метрики-снимки (спец. assistant, 13.5) раз в
/// <see cref="SpendOptions.MonitorInterval"/> — см. <see cref="BillingMonitor"/>.
///
/// Отдельный воркер, а не проход суточной чистки: сутки — слишком редко для остатка
/// у поставщика. И не команда очереди: работа по расписанию ничья, привязать её
/// к доменному событию не к чему. Инстансов API может быть несколько — каждый снимает
/// то же самое из той же базы, и приборы у них совпадают
/// </summary>
public class BillingMonitorWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SpendOptions> options,
    ILogger<BillingMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.MonitorInterval);

        try
        {
            // Первый снимок сразу после старта: до него приборы пусты
            do
            {
                await Check(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста — не ошибка
        }
    }

    private async Task Check(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BillingMonitor>().Check(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Упавший проход не должен ронять хост: приборы покажут прошлый снимок,
            // следующий проход — через интервал
            logger.LogError(e, "Billing monitor check failed");
        }
    }
}
