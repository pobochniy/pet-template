namespace PetTemplate.Services.Assistant.Models;

/// <summary>
/// Что значит исключение поставщика для размыкателя (спец. assistant, 16.2 и 16.5).
///
/// Транспорт у каждого адаптера <c>IChatClient</c> свой и бросает свои исключения:
/// OpenAI-совместимый — <c>ClientResultException</c> и <c>HttpRequestException</c>,
/// чужой SDK — что угодно. Размыкатель про типы не знает: разбор — здесь, и адаптер
/// несовместимого поставщика приносит свой разбор вместе с собой. Умолчание —
/// <see cref="OpenAiErrorClassifier"/>
/// </summary>
public interface IProviderErrorClassifier
{
    /// <summary>
    /// Отказ поставщика: сеть, таймаут, 5xx, отказ авторизации, 408/429 — модель надо
    /// размыкать. Наш запрос (прочие 4xx) и «Остановить» (<paramref name="ct"/> отменён) —
    /// не отказ
    /// </summary>
    bool IsFailure(Exception error, CancellationToken ct);

    /// <summary>
    /// Поставщик ответил кодом, пусть и отказом в нашем запросе: он жив, и проба
    /// размыкателя удалась
    /// </summary>
    bool Answered(Exception error);
}
