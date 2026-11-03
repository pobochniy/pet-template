namespace PetTemplate.Services.Assistant.Plans;

/// <summary>
/// Реестр операций плана (спец. assistant, 7.1): все <see cref="IPlanOperationHandler"/>,
/// которые принесли разделы проекта. Пуст — пишущих инструментов нет, и <c>apply_plan</c>
/// модели не показывается вовсе
/// </summary>
public class PlanOperations(IEnumerable<IPlanOperationHandler> handlers)
{
    private readonly Dictionary<string, IPlanOperationHandler> _handlers =
        handlers.ToDictionary(x => x.Kind, StringComparer.Ordinal);

    public bool IsEmpty => _handlers.Count == 0;

    /// <summary>Обработчик вида; <c>null</c> — такой операции в этой сборке нет</summary>
    public IPlanOperationHandler? Find(string kind) => _handlers.GetValueOrDefault(kind);
}
