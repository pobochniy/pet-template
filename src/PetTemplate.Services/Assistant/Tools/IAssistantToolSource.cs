namespace PetTemplate.Services.Assistant.Tools;

/// <summary>
/// Инструменты раздела проекта — часть раздела ассистента (спец. assistant, 7.1).
///
/// Раздел регистрирует свой источник в <c>IAssistantSection.Register</c> (<c>section.Tools&lt;T&gt;()</c>,
/// scoped): читающие инструменты отвечают данными, пишущие кладут операцию в план
/// (<c>PlanCollector.Propose</c>) и приносят к ней <c>IPlanOperationHandler</c>.
/// Права каждого инструмента — в <see cref="AssistantTool.Permissions"/>: проверяет их ядро.
/// Правила для модели — «сначала найди, потом правь», откуда брать идентификаторы —
/// едут в системный промпт отдельным абзацем (<see cref="Instructions"/>).
/// </summary>
public interface IAssistantToolSource
{
    /// <summary>
    /// Абзац системного промпта про инструменты раздела. Пусто — правил сверх общих нет.
    /// Только текст раздела: данных людей сюда не кладут (раздел 9)
    /// </summary>
    string Instructions { get; }

    /// <summary>
    /// Инструменты раздела с правами. Имя — то, что видит модель, и уникально во всём
    /// каталоге (<c>find_notes</c>, <c>create_note</c>); переименование меняет промпт всем
    /// поставщикам сразу
    /// </summary>
    IEnumerable<AssistantTool> Tools();
}
