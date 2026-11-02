namespace PetTemplate.Api.Workers;

/// <summary>
/// Разбор очереди команд (<see cref="CommandQueueWorker"/>). Значения — те, что у пакета
/// <c>LocalQueue</c> по умолчанию, кроме опроса: у пакета он 3 секунды, здесь 2 —
/// письмо подтверждения человек ждёт, глядя в почту
/// </summary>
public class CommandQueueOptions
{
    /// <summary>Пауза после пустой выборки</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Сколько строк берётся за одну выборку</summary>
    public int PrefetchCount { get; set; } = 10;

    /// <summary>
    /// На сколько строка уходит из выборки, пока её обрабатывают. Заведомо больше одной
    /// отправки письма: истёк раньше — строку взяли бы второй раз
    /// </summary>
    public TimeSpan InvisibilityTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Как часто снимать сводку по таблице для метрик длины очереди и числа попыток
    /// (<see cref="CommandQueueMetrics"/>) — как у пакета, 15 секунд: экспорт метрик
    /// раз в минуту, чаще незачем
    /// </summary>
    public TimeSpan SummaryInterval { get; set; } = TimeSpan.FromSeconds(15);
}
