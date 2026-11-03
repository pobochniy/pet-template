using LocalQueue.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Api.Workers;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Tests.Arranges;
using LocalQueue;

namespace PetTemplate.Tests.Features.Queue;

/// <summary>
/// Свои воркеры очереди и аренда строки (спец. infrastructure, 2.2; спец. assistant, раздел 3):
/// долгая команда одного воркера не держит другой, а обработчик продлевает аренду своей
/// строки сам (<c>ICommandLease</c> пакета). Проверяется обвязка шаблона
/// (<c>AddCommandQueueWorker</c>), а не сам пакет — у него свои тесты
/// </summary>
public class QueueLanesWorker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Один воркер занят долгой командой, а команда другого, поставленная позже,
    /// уже разобрана: ход ассистента не стоит за реиндексом поиска
    /// </summary>
    [Fact]
    public async Task OwnLaneDoesNotWaitForDefaultLane()
    {
        // Arrange
        var gate = new Gate();
        var slow = Row(typeof(SlowCommand), DateTime.UtcNow.AddMinutes(-1));
        var fast = Row(typeof(FastCommand), DateTime.UtcNow);

        // Act
        var db = await Given.WorkerClient(
            x =>
            {
                x.LocalQueue.Add(slow);
                x.LocalQueue.Add(fast);
            },
            services => services
                .AddSingleton(gate)
                .AddCommandQueueWorker(h => h.Add<SlowCommand, SlowHandler>())
                .AddCommandQueueWorker(h => h.Add<FastCommand, FastHandler>(), prefetchCount: 1));

        // Assert
        await gate.Entered.Task.WaitAsync(Timeout, CancellationToken.None);
        await WaitGone(db, fast.Id);
        Assert.True(await db.LocalQueue.AnyAsync(x => x.Id == slow.Id, CancellationToken.None));
        gate.Release.SetResult();
        await WaitGone(db, slow.Id);
    }

    /// <summary>
    /// Обработчик продлил аренду — строка спрятана до нового срока, и второй цикл
    /// её не возьмёт, пока первый ещё работает
    /// </summary>
    [Fact]
    public async Task HandlerExtendsLeaseOfItsRow()
    {
        // Arrange
        var gate = new Gate();
        var row = Row(typeof(LeasingCommand), DateTime.UtcNow);
        var before = DateTime.UtcNow;

        // Act
        var db = await Given.WorkerClient(
            x => x.LocalQueue.Add(row),
            services => services
                .AddSingleton(gate)
                .AddCommandQueueWorker(h => h.Add<LeasingCommand, LeasingHandler>()));

        // Assert
        await gate.Entered.Task.WaitAsync(Timeout, CancellationToken.None);
        var locked = await db.LocalQueue.AsNoTracking()
            .Where(x => x.Id == row.Id)
            .Select(x => x.LockedTillUtc)
            .SingleAsync(CancellationToken.None);
        // Аренду ставит время базы, а не хоста: часы контейнера расходятся с хостом на миллисекунды,
        // поэтому нижней границе — запас в секунды, при часовой аренде он ничего не скрывает
        Assert.InRange(locked!.Value, before.AddHours(1).AddSeconds(-5), DateTime.UtcNow.AddHours(1.5));
        gate.Release.SetResult();
        await WaitGone(db, row.Id);
    }

    private static Db.Entity.LocalQueue Row(Type command, DateTime createdAtUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        Data = "{}",
        CommandType = command.FullName!,
        CreatedAtUtc = createdAtUtc
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

    /// <summary>Задвижка: обработчик сообщает, что вошёл, и ждёт, пока тест отпустит</summary>
    public sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [CommandType("test-lanes-slow")]
    public class SlowCommand : ICommand;

    [CommandType("test-lanes-fast")]
    public class FastCommand : ICommand;

    [CommandType("test-lanes-leasing")]
    public class LeasingCommand : ICommand;

    private sealed class SlowHandler(Gate gate) : ICommandHandler<SlowCommand>
    {
        public async Task Handle(SlowCommand command, CancellationToken ct)
        {
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(ct);
        }
    }

    private sealed class FastHandler : ICommandHandler<FastCommand>
    {
        public Task Handle(FastCommand command, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class LeasingHandler(Gate gate, ICommandLease lease) : ICommandHandler<LeasingCommand>
    {
        public async Task Handle(LeasingCommand command, CancellationToken ct)
        {
            await lease.Extend(TimeSpan.FromHours(1), ct);
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(ct);
        }
    }
}
