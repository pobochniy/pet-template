using System.Text.Json;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Сжать тред ассистента (спец. assistant, 12.4): ставится после хода, когда история
/// перевалила порог. Повтор безопасен — сжатый тред второй раз не сворачивается
/// </summary>
public class AssistantCompactionCommand
{
    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public override string ToString() => JsonSerializer.Serialize(this);
}
