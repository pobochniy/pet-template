namespace PetTemplate.Shared.Options;

/// <summary>
/// Размыкатель моделей (спец. assistant, 16.2): сколько отказов подряд размыкают
/// модель и на сколько. Состояние живёт в строке <c>ai_models</c>, а не здесь: инстансов
/// API может быть несколько, и размыкать надо для всех сразу.
///
/// Умолчания — значения спеки. В <c>appsettings.json</c> лежат примеры.
/// </summary>
public class ModelBreakerOptions
{
    /// <summary>Сколько отказов подряд размыкают модель</summary>
    public int FailuresToOpen { get; set; } = 3;

    /// <summary>Первая пауза после размыкания</summary>
    public TimeSpan OpenFor { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Потолок паузы: неудачный пробный ход удваивает её, но не дальше этого
    /// </summary>
    public TimeSpan OpenForMax { get; set; } = TimeSpan.FromMinutes(30);
}
