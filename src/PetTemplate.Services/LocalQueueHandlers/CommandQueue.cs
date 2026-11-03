using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Постановка команды в <c>LocalQueue</c> тем же <c>DbContext</c>, что и доменные изменения:
/// команда уезжает в базу их же <c>SaveChanges</c>, то есть транзакцией (outbox, спец. mail, 4).
/// Отдельный <c>SaveChanges</c> на команду сломал бы всю затею: токен лёг бы, а письмо — нет.
///
/// Тело и тип строки задаются только здесь: тело — JSON с умолчаниями <c>System.Text.Json</c>
/// (их же ждёт десериализатор пакета), тип — <see cref="ICommand.QueueName"/>, а не имя
/// класса (спец. infrastructure, 2.2)
/// </summary>
public static class CommandQueue
{
    public static void Enqueue<T>(this ApplicationContext context, T command) where T : ICommand =>
        context.LocalQueue.Add(Row(command));

    /// <summary>
    /// Постановка сразу в базу, мимо трекера, — для путей на <c>ExecuteDelete</c>
    /// (<c>WorkspaceService.PurgeContents</c>): их транзакцию держит вызывающий, и строка
    /// очереди уходит в ней же, а чужие несохранённые правки трекера не уезжают раньше времени
    /// </summary>
    public static async Task EnqueueNow<T>(this ApplicationContext context, T command, CancellationToken ct)
        where T : ICommand
    {
        var row = Row(command);
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO local_queue (id, data, command_type, created_at_utc, try_count) VALUES ({row.Id}, {row.Data}::jsonb, {row.CommandType}, {row.CreatedAtUtc}, 0)",
            ct);
    }

    /// <summary>Тип строки очереди у команды <typeparamref name="T"/></summary>
    public static string TypeOf<T>() where T : ICommand => T.QueueName;

    private static Db.Entity.LocalQueue Row<T>(T command) where T : ICommand => new()
    {
        // v7: ключи растут, вставки идут в конец индекса, а не в случайное место.
        // Порядок разбора этим не задаётся: воркер сортирует команды по CreatedAtUtc
        Id = Guid.CreateVersion7(),
        Data = JsonSerializer.Serialize(command),
        CreatedAtUtc = DateTime.UtcNow,
        CommandType = T.QueueName
    };
}
