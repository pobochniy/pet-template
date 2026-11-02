using System.Diagnostics.Metrics;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Метрики очереди команд (спец. infrastructure, 10): сколько команд ждёт, сколько
/// раз бралась самая упрямая, сколько команда ждала до выполнения и сколько выполнялась.
///
/// Имена инструментов, единицы, метка <c>commandType</c> и границы гистограмм — ровно
/// как у пакета <c>LocalQueue.OpenTelemetry</c> (<c>LocalQueueMetrics</c>):
/// дашборд «Очередь команд» (<c>IaC/grafana/dashboards/queue.json</c>) ложится на них как есть,
/// и когда воркер заменится пакетом (<c>tech-debt/localqueue-postgres.md</c>), графики
/// не прервутся. Свой метр, а не статический, как у пакета: у каждого хоста в тестах
/// свой экземпляр, и чужие команды в его измерения не попадают
/// </summary>
public sealed class CommandQueueMetrics : IDisposable
{
    public const string MeterName = "PetTemplate.CommandQueue";

    public const string ProcessingTimeName = "local-command-queue-processing-time";
    public const string ProcessingLagName = "local-command-queue-processing-lag";
    public const string CompletedName = "local-command-queue-completed";
    public const string LengthName = "local-command-queue-length";
    public const string MaxTryCountName = "local-command-queue-max-try-count";

    /// <summary>
    /// Границы гистограмм, секунды, — пакетные (<c>Instruments.HistogramBoundaries</c>):
    /// от письма за десятые доли секунды до команды, которая ждала повтора минуты
    /// </summary>
    public static readonly double[] HistogramBoundaries = [0.1, 0.5, 1, 2, 5, 10, 30, 60, 2 * 60, 5 * 60, 10 * 60];

    /// <summary>
    /// Метка <c>dataSource</c> у счётчиков из таблицы, как у пакета. Таблица одна —
    /// метка одна
    /// </summary>
    private const string DataSource = "local_queue";

    private readonly Meter _meter = new(MeterName);
    private readonly Histogram<double> _processingTime;
    private readonly Histogram<double> _processingLag;
    private readonly Counter<int> _completed;

    /// <summary>
    /// Последняя сводка по таблице: тип команды → (сколько строк, наибольшее число попыток).
    /// Заменяется целиком, а не правится по ключу: наблюдатель читает её в любой момент
    /// и половину старой сводки с половиной новой не увидит
    /// </summary>
    private IReadOnlyDictionary<string, QueueSummary> _summary = new Dictionary<string, QueueSummary>();

    public CommandQueueMetrics()
    {
        _processingTime = _meter.CreateHistogram<double>(
            ProcessingTimeName, "seconds", "Command completion time in seconds by type");
        _processingLag = _meter.CreateHistogram<double>(
            ProcessingLagName, "seconds", "Command completion lag in seconds by type from creation to completion");
        _completed = _meter.CreateCounter<int>(
            CompletedName, "count", "Number of command handled by type");

        _meter.CreateObservableGauge(
            LengthName, () => Observe(x => x.Count), "count", "Number of commands in storage by type");
        _meter.CreateObservableGauge(
            MaxTryCountName, () => Observe(x => x.MaxTryCount), "count", "Count of command processing tries");
    }

    /// <summary>
    /// Команда выполнена и убрана из таблицы. Упавшая не считается — как у пакета:
    /// её видно по длине очереди и числу попыток
    /// </summary>
    public void Completed(string commandType, TimeSpan processingTime, TimeSpan processingLag)
    {
        var tag = new KeyValuePair<string, object?>("commandType", commandType);
        _processingTime.Record(processingTime.TotalSeconds, tag);
        _processingLag.Record(processingLag.TotalSeconds, tag);
        _completed.Add(1, tag);
    }

    /// <summary>
    /// Новая сводка по таблице. Типы со своим обработчиком видны и при пустой очереди —
    /// нулём, а не пропуском; тип, чьи строки разобрали, тоже падает в ноль, а не замирает
    /// на последнем значении. Строки без обработчика попадают в сводку как есть: они ждут,
    /// пока их разберут руками, и как раз их и надо видеть
    /// </summary>
    public void Summary(IEnumerable<QueueSummary> rows, IEnumerable<string> handledTypes)
    {
        var next = new Dictionary<string, QueueSummary>();

        foreach (var type in handledTypes.Concat(_summary.Keys))
        {
            next[type] = new QueueSummary(type, 0, 0);
        }

        foreach (var row in rows)
        {
            next[row.CommandType] = row;
        }

        _summary = next;
    }

    private IEnumerable<Measurement<int>> Observe(Func<QueueSummary, int> value) =>
        _summary.Values.Select(x => new Measurement<int>(
            value(x),
            new KeyValuePair<string, object?>("commandType", x.CommandType),
            new KeyValuePair<string, object?>("dataSource", DataSource)));

    public void Dispose() => _meter.Dispose();
}

/// <summary>Строки одного типа команды в таблице очереди</summary>
public sealed record QueueSummary(string CommandType, int Count, int MaxTryCount);
