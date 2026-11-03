namespace PetTemplate.Services.Commands;

/// <summary>
/// Ход ассистента (спец. assistant, раздел 3). Ставится тем же сохранением, что
/// и реплика человека с прогоном (outbox); <see cref="RunId"/> — ключ идемпотентности:
/// повтор команды на законченном ходе не делает ничего
/// </summary>
public class AssistantRunCommand : ICommand
{
    public static string QueueName => "assistant-run";

    public Guid RunId { get; set; }
}
