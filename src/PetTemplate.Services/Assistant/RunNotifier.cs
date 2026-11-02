using System.Collections.Concurrent;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Сигнал «у хода что-то изменилось» для долгого опроса (спец. assistant, раздел 14).
///
/// Вместо опроса раз в секунду клиент зовёт <c>GET Run/{id}?wait=20</c>, а сервер ждёт
/// здесь: короткий ход отвечает <b>в момент завершения</b>, а не на следующем тике,
/// тридцатишаговый укладывается в десяток запросов вместо полутора сотен.
///
/// Сигнал живёт в пределах процесса, поэтому он — ускорение, а не гарантия: ожидающий
/// всё равно перечитывает базу раз в пару секунд, и это то, что работает при нескольких
/// инстансах API.
/// </summary>
public class RunNotifier
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _runs = new();

    /// <summary>
    /// Ждать изменения не дольше <paramref name="timeout"/>. Ничего не пришло —
    /// возвращаемся молча: решает всё равно тот, кто перечитает базу
    /// </summary>
    public async Task Wait(Guid runId, TimeSpan timeout, CancellationToken ct)
    {
        var waiter = _runs.GetOrAdd(runId,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        using var timer = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);

        await Task.WhenAny(waiter.Task, Task.Delay(Timeout.Infinite, linked.Token))
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    /// <summary>
    /// Шаг сделан, ход кончился, эскалация случилась — будим всех, кто ждёт
    /// </summary>
    public void Notify(Guid runId)
    {
        if (_runs.TryRemove(runId, out var waiter)) waiter.TrySetResult();
    }
}
