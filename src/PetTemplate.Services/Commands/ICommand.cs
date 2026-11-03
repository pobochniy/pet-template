namespace PetTemplate.Services.Commands;

/// <summary>
/// Команда очереди (спец. infrastructure, 2.2). Ставится только через
/// <c>CommandQueue.Enqueue</c>: тело — JSON одним сериализатором на всех, тип строки —
/// <see cref="QueueName"/>, а не имя класса.
///
/// Имя — то, что лежит в <c>local_queue.command_type</c> и ждёт разбора, может быть,
/// часами: переименование класса или переезд в другое пространство имён строк не теряет.
/// Само имя не меняется никогда — у поставленных строк его не переписать без миграции
/// </summary>
public interface ICommand
{
    /// <summary>Устойчивое имя типа команды в очереди: kebab-case, уникально на приложение</summary>
    static abstract string QueueName { get; }
}
