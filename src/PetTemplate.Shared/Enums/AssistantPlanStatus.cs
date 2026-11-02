namespace PetTemplate.Shared.Enums;

/// <summary>
/// Состояние плана ассистента (спец. assistant, 12.2.1).
///
/// Незакрытый план в треде один: новый переводит предыдущий в <see cref="Cancelled"/>.
///
/// Номера уезжают в базу — добавлять только в конец.
/// </summary>
public enum AssistantPlanStatus
{
    /// <summary>
    /// Собран частично: ход закончился уточняющим вопросом или остановкой (5.2)
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Показан человеку и ждёт согласия репликой (5.1)
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Применён — результат каждой операции лежит в <c>Result</c>
    /// </summary>
    Applied = 2,

    /// <summary>
    /// Отменён: отказ, смена темы, новая просьба или очистка треда
    /// </summary>
    Cancelled = 3
}
