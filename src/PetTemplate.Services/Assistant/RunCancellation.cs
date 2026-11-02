using System.Collections.Concurrent;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Живые прогоны этого процесса (спец. assistant, раздел 3).
///
/// Остановка доходит до обработчика двумя путями. Гарантия — статус прогона в базе:
/// его обработчик перечитывает перед каждым шагом, и это переживает несколько
/// инстансов API. Быстрота — вот этот словарь: эндпоинт «Остановить» дёргает
/// <see cref="Cancel"/>, и уже идущий запрос к модели обрывается сразу, а не через
/// полминуты, когда модель дораздумает.
///
/// Синглтон: прогон живёт в воркере, а останавливают его из запроса — это разные
/// области видимости в одном процессе.
/// </summary>
public class RunCancellation
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runs = new();

    /// <summary>
    /// Связывает прогон с токеном воркера: остановка процесса обрывает ход так же,
    /// как кнопка
    /// </summary>
    public CancellationTokenSource Register(Guid runId, CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runs[runId] = source;

        return source;
    }

    /// <summary>
    /// Оборвать идущий ход. Прогона нет в словаре — значит, он идёт в другом инстансе
    /// или уже кончился: там его остановит статус в базе
    /// </summary>
    public void Cancel(Guid runId)
    {
        if (_runs.TryGetValue(runId, out var source)) source.Cancel();
    }

    public void Forget(Guid runId) => _runs.TryRemove(runId, out _);
}
