using PetTemplate.Services.Help;
using PetTemplate.Shared.Options;
using Microsoft.Extensions.Options;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Опрос справки (спец. help, 6.4): раз в <see cref="HelpOptions.PollInterval"/>
/// читает манифест опубликованного выпуска и, если коммит сменился, пересобирает индекс
/// в памяти. Справку приносит выкатка — к GitHub API не ходит. Первый проход —
/// сразу на старте
/// </summary>
public class HelpIndexWorker(HelpSearch help, IOptions<HelpOptions> options, ILogger<HelpIndexWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);

        try
        {
            // Первый проход сразу: после рестарта справка нужна с первого хода
            do
            {
                try
                {
                    await help.Refresh(stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    // Сломанный выпуск не роняет API: ищет прежний индекс, а следующий
                    // проход попробует снова
                    logger.LogError(e, "Help index rebuild failed, keeping the previous version");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста — не ошибка
        }
    }
}
