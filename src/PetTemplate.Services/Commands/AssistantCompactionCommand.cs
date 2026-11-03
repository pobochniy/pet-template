using LocalQueue;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Сжать тред ассистента (спец. assistant, 12.4): ставится после хода, когда история
/// перевалила порог. Повтор безопасен — сжатый тред второй раз не сворачивается
/// </summary>
[CommandType("assistant-compaction")]
public class AssistantCompactionCommand : ICommand
{
    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }
}
