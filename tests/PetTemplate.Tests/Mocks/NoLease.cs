using PetTemplate.Services.LocalQueueHandlers;

namespace PetTemplate.Tests.Mocks;

/// <summary>Аренда строки очереди у обработчика, позванного мимо воркера: строки нет — продлевать нечего</summary>
public sealed class NoLease : ICommandLease
{
    public int Extended { get; private set; }

    public Task Extend(TimeSpan duration, CancellationToken ct)
    {
        Extended++;

        return Task.CompletedTask;
    }
}
