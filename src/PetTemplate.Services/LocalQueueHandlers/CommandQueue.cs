using PetTemplate.Db.Entity;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Постановка команды в <c>LocalQueue</c> тем же <c>DbContext</c>, что и доменные изменения:
/// команда уезжает в базу их же <c>SaveChanges</c>, то есть транзакцией (outbox, спец. mail, 4).
/// Отдельный <c>SaveChanges</c> на команду сломал бы всю затею: токен лёг бы, а письмо — нет
/// </summary>
public static class CommandQueue
{
    public static void Enqueue(this ApplicationContext context, object command)
    {
        context.LocalQueue.Add(new Db.Entity.LocalQueue
        {
            // v7: ключи растут, вставки идут в конец индекса, а не в случайное место.
            // Порядок разбора этим не задаётся: воркер сортирует команды по CreatedAtUtc
            Id = Guid.CreateVersion7(),
            Data = command.ToString()!,
            CreatedAtUtc = DateTime.UtcNow,
            CommandType = command.GetType().FullName!
        });
    }
}
