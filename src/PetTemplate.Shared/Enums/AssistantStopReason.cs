namespace PetTemplate.Shared.Enums;

/// <summary>
/// Чем закончился ход (спец. assistant, 12.3). Каждый упёршийся предохранитель
/// виден человеку текстом, а не только в метриках (раздел 8).
///
/// Номера уезжают в базу — добавлять только в конец.
/// </summary>
public enum AssistantStopReason
{
    /// <summary>Ход дошёл до ответа сам</summary>
    Completed = 0,

    /// <summary>Упёрся в <c>StepsHard</c> или в детектор повторов (раздел 8)</summary>
    StepLimit = 1,

    /// <summary>Упёрся в потолок токенов на ход (раздел 8)</summary>
    TokenLimit = 2,

    /// <summary>Человек нажал «Остановить»</summary>
    UserStopped = 3,

    /// <summary>
    /// Поставщик не ответил — после того, как исчерпаны попытки очереди на команду,
    /// или ходить было некуда: все модели разомкнуты или выключены (раздел 3, 16.2)
    /// </summary>
    ProviderError = 4
}
