namespace PetTemplate.Services.Commands;

/// <summary>
/// Унести из Qdrant все точки снесённого пространства (спец. search, 4). Ставит
/// <c>WorkspaceService.PurgeContents</c> той же транзакцией, что сносит строки: в payload
/// точки лежит текст чанка, и снесённое пространство не должно оставаться в индексе
/// ни строчкой — что бы ни забыл раздел со своими командами индексации.
///
/// Повтор безопасен: удаление по фильтру у пустого пространства ничего не находит
/// </summary>
public class SearchPurgeWorkspaceCommand : ICommand
{
    public static string QueueName => "search-purge-workspace";

    public Guid WorkspaceId { get; set; }
}
