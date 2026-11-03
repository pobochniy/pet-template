using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using LocalQueue.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Api.Workers;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Tests.Arranges;

namespace PetTemplate.Tests.Features.Queue;

/// <summary>
/// Хранилище воркера шаблона поверх пакетного (спец. infrastructure, 2.2): строки с устойчивым
/// именем команды и строки под <c>FullName</c>, поставленные до перехода, разбираются одинаково,
/// а команда, исчерпавшая попытки, откладывается насовсем, а не ходит по кругу
/// </summary>
public class QueueStorageWorker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Новая строка — под именем из <c>ICommand.QueueName</c>, старая — под именем класса:
    /// обработчик один, и разобраны обе
    /// </summary>
    [Fact]
    public async Task NamedAndLegacyRowsAreBothHandled()
    {
        // Arrange
        var calls = new Calls();
        var named = Row(CommandQueue.TypeOf<NamedCommand>(), tryCount: 0);
        var legacy = Row(typeof(NamedCommand).FullName!, tryCount: 0);

        // Act
        var db = await Given.WorkerClient(
            x =>
            {
                x.LocalQueue.Add(named);
                x.LocalQueue.Add(legacy);
            },
            services => services
                .AddSingleton(calls)
                .AddCommandQueueWorker(h => h.Add<NamedCommand, CountingHandler<NamedCommand>>()));

        // Assert
        await WaitGone(db, named.Id);
        await WaitGone(db, legacy.Id);
        Assert.Equal(2, calls.Count);
    }

    /// <summary>
    /// Строку уже брали столько раз, сколько можно (например, процесс дважды умер посреди
    /// неё): обработчик её больше не получает, она отложена насовсем и посчитана брошенной
    /// </summary>
    [Fact]
    public async Task RowOutOfAttemptsIsParked()
    {
        // Arrange
        using var gaveUp = new GaveUpListener();
        var calls = new Calls();
        var row = Row(CommandQueue.TypeOf<ParkedCommand>(), tryCount: 2);

        // Act
        var db = await Given.WorkerClient(
            x => x.LocalQueue.Add(row),
            services => services
                .AddSingleton(calls)
                .AddCommandQueueWorker(h => h.Add<ParkedCommand, CountingHandler<ParkedCommand>>(maxAttempts: 2)));

        // Assert
        var parked = await WaitParked(db, row.Id);
        Assert.Equal(3, parked.TryCount);
        Assert.Equal(0, calls.Count);
        Assert.Equal(1, await gaveUp.WaitFor(typeof(ParkedCommand).FullName!));
    }

    /// <summary>
    /// Команда, которая падает всегда, повторяется до предела попыток и откладывается:
    /// чанк, который эмбеддер не возьмёт никогда, не ходит к нему раз в час вечно
    /// </summary>
    [Fact]
    public async Task AlwaysFailingCommandGivesUpAfterMaxAttempts()
    {
        // Arrange
        var calls = new Calls();
        var row = Row(CommandQueue.TypeOf<PoisonCommand>(), tryCount: 0);

        // Act
        var db = await Given.WorkerClient(
            x => x.LocalQueue.Add(row),
            services => services
                .AddSingleton(calls)
                .AddCommandQueueWorker(h => h.Add<PoisonCommand, PoisonHandler>(
                    retryAfterFailure: TimeSpan.FromMilliseconds(50), maxAttempts: 2)));

        // Assert
        await WaitParked(db, row.Id);
        Assert.Equal(2, calls.Count);
    }

    private static Db.Entity.LocalQueue Row(string commandType, int tryCount) => new()
    {
        Id = Guid.CreateVersion7(),
        Data = "{}",
        CommandType = commandType,
        CreatedAtUtc = DateTime.UtcNow,
        TryCount = tryCount
    };

    private static async Task WaitGone(ApplicationContext db, Guid id)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (await db.LocalQueue.AsNoTracking().AnyAsync(x => x.Id == id, CancellationToken.None))
        {
            Assert.True(DateTime.UtcNow < deadline, "command was not processed in time");
            await Task.Delay(50, CancellationToken.None);
        }
    }

    /// <summary>Строка отложена «насовсем» — дальше, чем на год</summary>
    private static async Task<Db.Entity.LocalQueue> WaitParked(ApplicationContext db, Guid id)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var row = await db.LocalQueue.AsNoTracking().SingleAsync(x => x.Id == id, CancellationToken.None);
            if (row.LockedTillUtc > DateTime.UtcNow.AddYears(1)) return row;

            Assert.True(DateTime.UtcNow < deadline, "command was not parked in time");
            await Task.Delay(50, CancellationToken.None);
        }
    }

    public sealed class Calls
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add() => Interlocked.Increment(ref _count);
    }

    public class NamedCommand : ICommand
    {
        public static string QueueName => "test-storage-named";
    }

    public class ParkedCommand : ICommand
    {
        public static string QueueName => "test-storage-parked";
    }

    public class PoisonCommand : ICommand
    {
        public static string QueueName => "test-storage-poison";
    }

    private sealed class CountingHandler<T>(Calls calls) : ICommandHandler<T>
    {
        public Task Handle(T command, CancellationToken ct)
        {
            calls.Add();

            return Task.CompletedTask;
        }
    }

    private sealed class PoisonHandler(Calls calls) : ICommandHandler<PoisonCommand>
    {
        public Task Handle(PoisonCommand command, CancellationToken ct)
        {
            calls.Add();

            throw new InvalidOperationException("poison chunk");
        }
    }

    /// <summary>
    /// Счётчик брошенных команд шаблона. Метр — один на хост, а тесты идут параллельно,
    /// поэтому измерения отбираются по типу команды теста
    /// </summary>
    private sealed class GaveUpListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, long> _counts = new();

        public GaveUpListener()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CommandQueueMetrics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "commandType")
                        _counts.AddOrUpdate((string)tag.Value!, value, (_, sum) => sum + value);
                }
            });
            _listener.Start();
        }

        public async Task<long> WaitFor(string commandType)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!_counts.ContainsKey(commandType))
            {
                Assert.True(DateTime.UtcNow < deadline, "give-up was not counted in time");
                await Task.Delay(50, CancellationToken.None);
            }

            return _counts[commandType];
        }

        public void Dispose() => _listener.Dispose();
    }
}
