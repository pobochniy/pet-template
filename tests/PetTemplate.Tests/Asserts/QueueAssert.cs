using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;

namespace PetTemplate.Tests.Asserts;

/// <summary>
/// Команды, поставленные в <c>LocalQueue</c>. Разбирает их воркер, а в API-тестах он снят
/// (<see cref="ApiApplicationFactory{TStartup}"/>), поэтому API-тест проверяет не результат
/// обработки, а сам факт постановки: команда, доехавшая до базы вместе с доменным
/// изменением, — это и есть контракт. Обработку проверяют тесты воркера
/// (<c>Features/Account/MailWorker.cs</c>)
/// </summary>
public static class QueueAssert
{
    /// <summary>Письма: ставятся тем же сохранением, что и токен (спец. mail, 4)</summary>
    public static Task<List<SendMailCommand>> Mails(ApplicationContext db) =>
        Commands<SendMailCommand>(db);

    /// <summary>Команды одного типа — для очередей, у которых своего метода здесь нет (поиск)</summary>
    public static async Task<List<T>> Commands<T>(ApplicationContext db) where T : ICommand
    {
        var commandType = CommandQueue.TypeOf<T>();
        var rows = await db.LocalQueue
            .AsNoTracking()
            .Where(x => x.CommandType == commandType)
            .ToListAsync();

        return rows.Select(x => JsonSerializer.Deserialize<T>(x.Data)!).ToList();
    }
}
