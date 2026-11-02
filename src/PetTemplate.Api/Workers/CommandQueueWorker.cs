using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.LocalQueueHandlers;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Внутренняя очередь команд поверх таблицы <c>local_queue</c> (спец. mail, 4). Временная:
/// пакет <c>localqueue</c> без хранилища не работает, а хранилища под PostgreSQL у него пока
/// нет (<c>tech-debt/localqueue-postgres.md</c>). Устроена как пакет, чтобы замена прошла
/// без правок вокруг: та же таблица, та же постановка (<c>CommandQueue.Enqueue</c> — тем же
/// <c>SaveChanges</c>, что и доменное изменение), та же сигнатура обработчика
/// (<see cref="ICommandHandler{TCommand}"/>).
///
/// <list type="bullet">
/// <item>выборка — <c>UPDATE … FOR UPDATE SKIP LOCKED</c>: строку берёт один воркер, даже
/// если экземпляров API станет больше одного;</item>
/// <item>обработано — строка удаляется; исключение — строка остаётся и прячется на
/// <see cref="CommandHandlerRegistration.RetryAfterFailure"/>: повтор, а не потеря.
/// Числа попыток нет, как и у пакета: письмо, которое не ушло за сутки, видно в метриках;</item>
/// <item>упал сам процесс посреди обработки — строку через <see cref="CommandQueueOptions.InvisibilityTimeout"/>
/// возьмёт следующая выборка.</item>
/// </list>
///
/// Строки без обработчика (тип переименовали, обработчик убрали) не выбираются вовсе —
/// они ждут, пока их разберут руками; видно их в метрике длины очереди.
///
/// Разбор идёт дорожками (<see cref="CommandQueueLane"/>): у каждой свои типы команд,
/// свои циклы и своя аренда. Общая — <see cref="CommandQueueLanes.Default"/>, один цикл
/// с настройками <see cref="CommandQueueOptions"/>; долгие команды (ход ассистента)
/// заводят свою и не стоят за реиндексом поиска, а письма не стоят за ними. Долгий
/// обработчик продлевает аренду своей строки сам (<see cref="CommandLease"/>).
///
/// Рядом с разбором — сводка по таблице для метрик (<see cref="CommandQueueMetrics"/>),
/// как <c>StorageSummaryCollector</c> у пакета: длину очереди и число попыток видно,
/// даже когда разбор стоит
/// </summary>
public class CommandQueueWorker(
    IServiceScopeFactory scopeFactory,
    IEnumerable<CommandHandlerRegistration> registrations,
    IEnumerable<CommandQueueLane> lanes,
    IOptions<CommandQueueOptions> options,
    CommandQueueMetrics metrics,
    ILogger<CommandQueueWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, CommandHandlerRegistration> _handlers =
        registrations.ToDictionary(x => x.CommandType);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var all = Lanes(settings);

        var loops = all
            .Select(lane => (Lane: lane, Types: _handlers.Values
                .Where(x => x.Lane == lane.Name)
                .Select(x => x.CommandType)
                .ToArray()))
            .Where(x => x.Types.Length > 0)
            .SelectMany(x => Enumerable.Range(0, Math.Max(x.Lane.Workers, 1))
                .Select(_ => ProcessLoop(x.Lane, x.Types, settings, stoppingToken)))
            .ToList();

        loops.Add(SummaryLoop(settings, stoppingToken));

        await Task.WhenAll(loops);
    }

    /// <summary>
    /// Общая дорожка и заявленные. Обработчик на дорожке, которой нет, — ошибка сборки
    /// приложения, а не повод молча не разбирать его команды
    /// </summary>
    private List<CommandQueueLane> Lanes(CommandQueueOptions settings)
    {
        var all = new List<CommandQueueLane>
        {
            new(CommandQueueLanes.Default, 1, settings.PrefetchCount, settings.InvisibilityTimeout)
        };
        all.AddRange(lanes.Where(x => x.Name != CommandQueueLanes.Default));

        var unknown = _handlers.Values.FirstOrDefault(x => all.TrueForAll(lane => lane.Name != x.Lane));
        if (unknown != null)
            throw new InvalidOperationException($"Command {unknown.CommandType} is bound to unknown lane '{unknown.Lane}'");

        return all;
    }

    private async Task ProcessLoop(
        CommandQueueLane lane,
        string[] types,
        CommandQueueOptions settings,
        CancellationToken stoppingToken)
    {
        // Циклы дорожки и дорожки между собой — параллельно: без уступки первый же цикл
        // занял бы поток старта хоста до первой паузы
        await Task.Yield();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var processed = await ProcessBatch(lane, types, stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(settings.PollInterval, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста — не ошибка: недоделанные строки возьмёт следующий запуск
        }
    }

    /// <summary>
    /// Сводка по таблице раз в <see cref="CommandQueueOptions.SummaryInterval"/>, первая —
    /// сразу после старта. Не удалась — метрики держат прошлую, повтор на следующем тике
    /// </summary>
    private async Task SummaryLoop(CommandQueueOptions settings, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.SummaryInterval);

        try
        {
            do
            {
                await RecordSummary(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста
        }
    }

    private async Task RecordSummary(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            var rows = await db.LocalQueue
                .GroupBy(x => x.CommandType)
                .Select(g => new QueueSummary(g.Key, g.Count(), g.Max(x => x.TryCount)))
                .ToListAsync(ct);

            metrics.Summary(rows, _handlers.Keys);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogError(e, "Command queue summary failed");
        }
    }

    private async Task<int> ProcessBatch(CommandQueueLane lane, string[] types, CancellationToken ct)
    {
        List<Db.Entity.LocalQueue> batch;
        try
        {
            batch = await Fetch(lane, types, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Необработанное исключение из `ExecuteAsync` остановило бы хост целиком —
            // недоступная на минуту база уронила бы весь API. Повторим после паузы
            logger.LogError(e, "Command queue fetch failed");
            return 0;
        }

        foreach (var row in batch)
        {
            await Process(row, ct);
        }

        return batch.Count;
    }

    /// <summary>
    /// Взять свободные строки типов своей дорожки в порядке постановки и спрятать их
    /// на время обработки — одним запросом: между выборкой и блокировкой строку
    /// не перехватит ни второй цикл, ни второй экземпляр
    /// </summary>
    private async Task<List<Db.Entity.LocalQueue>> Fetch(CommandQueueLane lane, string[] types, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        var now = DateTime.UtcNow;
        var lockedTill = now + lane.InvisibilityTimeout;
        var count = Math.Max(lane.PrefetchCount, 1);

        return await db.LocalQueue
            .FromSql($"""
                UPDATE local_queue
                SET locked_till_utc = {lockedTill}, try_count = try_count + 1
                WHERE id IN (
                    SELECT id FROM local_queue
                    WHERE command_type = ANY({types})
                      AND (locked_till_utc IS NULL OR locked_till_utc <= {now})
                    ORDER BY created_at_utc
                    LIMIT {count}
                    FOR UPDATE SKIP LOCKED)
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    private async Task Process(Db.Entity.LocalQueue row, CancellationToken ct)
    {
        var handler = _handlers[row.CommandType];

        // Своя область на команду: обработчик и его `DbContext` живут одну команду,
        // и упавшая не оставляет следующей грязный трекер
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        scope.ServiceProvider.GetRequiredService<CommandLease>().RowId = row.Id;
        var started = Stopwatch.GetTimestamp();

        try
        {
            await handler.Handle(scope.ServiceProvider, row.Data, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogError(e, "Command {Id} of type {Type} failed, try {Try}", row.Id, row.CommandType, row.TryCount);
            await Postpone(db, row.Id, handler.RetryAfterFailure);
            return;
        }

        await db.LocalQueue.Where(x => x.Id == row.Id).ExecuteDeleteAsync(CancellationToken.None);

        // Время — обработка вместе с удалением строки, задержка — от постановки до конца,
        // повторы включительно: так считает пакет
        metrics.Completed(row.CommandType, Stopwatch.GetElapsedTime(started), DateTime.UtcNow - row.CreatedAtUtc);
    }

    /// <summary>
    /// Отложить упавшую строку. Без токена отмены: остановка хоста посреди записи
    /// оставила бы строку спрятанной на полный <see cref="CommandQueueOptions.InvisibilityTimeout"/>
    /// </summary>
    private async Task Postpone(ApplicationContext db, Guid id, TimeSpan delay)
    {
        try
        {
            var lockedTill = DateTime.UtcNow + delay;
            await db.LocalQueue
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x.SetProperty(q => q.LockedTillUtc, lockedTill), CancellationToken.None);
        }
        catch (Exception e)
        {
            // Не отложилась — строку и так вернёт истёкшая блокировка выборки
            logger.LogError(e, "Command {Id} could not be postponed", id);
        }
    }
}
