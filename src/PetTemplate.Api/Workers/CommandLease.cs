using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.LocalQueueHandlers;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Аренда строки, которую разбирает обработчик этой области (<see cref="ICommandLease"/>).
/// Строку называет воркер перед вызовом обработчика (<see cref="RowId"/>); обработчик,
/// позванный мимо воркера, продлевать нечего — и вызов ничего не делает.
///
/// Наружу пакет <c>LocalQueue</c> продления не даёт, и обработчику пришлось бы искать
/// свою строку по телу команды; своей очереди это не нужно — строка известна
/// </summary>
public sealed class CommandLease(ApplicationContext db) : ICommandLease
{
    public Guid? RowId { get; set; }

    public async Task Extend(TimeSpan duration, CancellationToken ct)
    {
        if (RowId is not { } id) return;

        var lockedTill = DateTime.UtcNow + duration;
        await db.LocalQueue
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(x => x.SetProperty(q => q.LockedTillUtc, lockedTill), ct);
    }
}
