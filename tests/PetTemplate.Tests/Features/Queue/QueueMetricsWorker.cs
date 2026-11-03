using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using LocalQueue.Diagnostics;
using LocalQueue.Processing;
using PetTemplate.Api.Workers;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Tests.Arranges;
using LocalQueue;

namespace PetTemplate.Tests.Features.Queue;

/// <summary>
/// Метрики очереди команд (спец. infrastructure, 10) — пакета <c>AsuTrack.LocalQueue</c>:
/// на их именах и метках стоит дашборд «Очередь команд». Проверяется, что воркеры шаблона
/// их пишут и что инструменты называются так, как их ждёт дашборд.
///
/// Метр пакета — один на процесс, а тесты идут параллельно — поэтому у каждого теста
/// свой тип команды, и измерения отбираются по нему
/// </summary>
public class QueueMetricsWorker
{
    private const string Completed = "local-command-queue-completed";
    private const string Length = "local-command-queue-length";
    private const string MaxTryCount = "local-command-queue-max-try-count";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Выполненная команда — в счётчике и обеих гистограммах под устойчивым именем команды
    /// (метка <c>commandType</c> дашборда); задержка считается от постановки, а не от взятия в работу
    /// </summary>
    [Fact]
    public async Task RecordsCompletedCommand()
    {
        // Arrange
        using var listener = new QueueListener();
        var commandType = CommandQueue.TypeOf<PingCommand>();
        var row = new Db.Entity.LocalQueue
        {
            Id = Guid.CreateVersion7(),
            Data = "{}",
            CommandType = commandType,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        // Act
        await Given.WorkerClient(
            db => db.LocalQueue.Add(row),
            services => services.AddCommandQueueWorker(h => h.Add<PingCommand, PingHandler>()));

        // Assert
        var completed = await listener.WaitFor(Completed, commandType);
        Assert.Equal(1, completed.Value);
        var lag = await listener.WaitFor(Instruments.CommandProcessingLagHistogramName, commandType);
        Assert.InRange(lag.Value, 60, 60 + Timeout.TotalSeconds);
        var time = await listener.WaitFor(Instruments.CommandProcessingTimeHistogramName, commandType);
        Assert.InRange(time.Value, 0, Timeout.TotalSeconds);
    }

    /// <summary>
    /// Длина очереди и попытки — из сводки по таблице, и по строкам без обработчика тоже:
    /// они ждут, пока их разберут руками, и видеть надо именно их
    /// </summary>
    [Fact]
    public async Task ReportsLengthAndTriesOfStuckCommands()
    {
        // Arrange
        using var listener = new QueueListener();
        var commandType = $"PetTemplate.Tests.Orphan{Guid.NewGuid():N}";

        // Act
        await Given.WorkerClient(db =>
        {
            db.LocalQueue.Add(Orphan(commandType, tryCount: 1));
            db.LocalQueue.Add(Orphan(commandType, tryCount: 3));
        });

        // Assert
        var length = await listener.WaitFor(Length, commandType);
        Assert.Equal(2, length.Value);
        // Источник — «база.схема.таблица»: у каждого теста своя база
        Assert.EndsWith(".public.local_queue", (string)length.Tags["dataSource"]!);
        var tries = await listener.WaitFor(MaxTryCount, commandType);
        Assert.Equal(3, tries.Value);
    }

    private static Db.Entity.LocalQueue Orphan(string commandType, int tryCount) => new()
    {
        Id = Guid.CreateVersion7(),
        Data = "{}",
        CommandType = commandType,
        CreatedAtUtc = DateTime.UtcNow,
        TryCount = tryCount
    };

    [CommandType("test-metrics-ping")]
    public class PingCommand : ICommand;

    private class PingHandler : ICommandHandler<PingCommand>
    {
        public Task Handle(PingCommand command, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed record Recorded(double Value, IReadOnlyDictionary<string, object?> Tags);

    /// <summary>Слушает метр очереди всех хостов процесса и копит измерения по инструменту</summary>
    private sealed class QueueListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, Recorded Measurement)> _recorded = new();

        public QueueListener()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == Instruments.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<int>((i, value, tags, _) => Record(i, value, tags));
            _listener.SetMeasurementEventCallback<double>((i, value, tags, _) => Record(i, value, tags));
            _listener.Start();
        }

        /// <summary>
        /// Первое измерение инструмента с этим типом команды. Наблюдаемые счётчики
        /// опрашиваются тут же, на каждом круге, — как их опросил бы экспорт
        /// </summary>
        public async Task<Recorded> WaitFor(string instrument, string commandType)
        {
            using var cts = new CancellationTokenSource(Timeout);
            while (true)
            {
                _listener.RecordObservableInstruments();
                var found = _recorded.FirstOrDefault(x =>
                    x.Instrument == instrument && Equals(x.Measurement.Tags.GetValueOrDefault("commandType"), commandType)
                    && (x.Measurement.Value > 0 || !IsGauge(instrument)));
                if (found.Measurement != null) return found.Measurement;

                await Task.Delay(100, cts.Token);
            }
        }

        /// <summary>Наблюдаемый счётчик до первой сводки отдаёт ноль — ждём настоящего значения</summary>
        private static bool IsGauge(string instrument) =>
            instrument is Length or MaxTryCount;

        private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags) copy[tag.Key] = tag.Value;
            _recorded.Enqueue((instrument.Name, new Recorded(Convert.ToDouble(value), copy)));
        }

        public void Dispose() => _listener.Dispose();
    }
}
