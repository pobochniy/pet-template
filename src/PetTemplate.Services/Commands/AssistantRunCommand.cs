using System.Text.Json;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Ход ассистента (спец. assistant, раздел 3). Ставится тем же сохранением, что
/// и реплика человека с прогоном (outbox); <see cref="RunId"/> — ключ идемпотентности:
/// повтор команды на законченном ходе не делает ничего
/// </summary>
public class AssistantRunCommand
{
    public Guid RunId { get; set; }

    public override string ToString() => JsonSerializer.Serialize(this);
}
