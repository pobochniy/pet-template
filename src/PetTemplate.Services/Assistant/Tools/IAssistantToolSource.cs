using Microsoft.Extensions.AI;

namespace PetTemplate.Services.Assistant.Tools;

/// <summary>
/// Инструменты раздела проекта — точка расширения каталога (спец. assistant, 7.1).
///
/// Раздел регистрирует свой источник (<c>AddAssistantTools&lt;T&gt;()</c>, scoped):
/// читающие инструменты отвечают данными, пишущие кладут операцию в план
/// (<c>PlanCollector.Propose</c>) и приносят к ней <c>IPlanOperationHandler</c>.
/// Правила для модели — «сначала найди, потом правь», откуда брать идентификаторы —
/// едут в системный промпт отдельным абзацем (<see cref="Instructions"/>).
/// </summary>
public interface IAssistantToolSource
{
    /// <summary>
    /// Абзац системного промпта про инструменты раздела. Пусто — правил сверх общих нет
    /// </summary>
    string Instructions { get; }

    /// <summary>
    /// Инструменты раздела. Имя — то, что видит модель, и уникально во всём каталоге
    /// (<c>find_notes</c>, <c>create_note</c>); переименование меняет промпт всем
    /// поставщикам сразу
    /// </summary>
    IEnumerable<AITool> Tools();
}
