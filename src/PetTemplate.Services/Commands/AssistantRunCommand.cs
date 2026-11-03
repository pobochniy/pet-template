using LocalQueue;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Ход ассистента (спец. assistant, раздел 3). Ставится тем же сохранением, что
/// и реплика человека с прогоном (outbox); <see cref="RunId"/> — ключ идемпотентности:
/// повтор команды на законченном ходе не делает ничего
/// </summary>
[CommandType("assistant-run")]
public class AssistantRunCommand : ICommand
{
    public Guid RunId { get; set; }
}
