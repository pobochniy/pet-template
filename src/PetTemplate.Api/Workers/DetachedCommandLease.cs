using LocalQueue.Processing;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Аренда без строки — когда очередь не разбирается (<see cref="CommandQueueOptions.Enabled"/>
/// выключен). <c>ICommandLease</c> регистрирует воркер пакета, а без воркеров обработчики
/// долгих команд (ход ассистента, сверка модели) не собрались бы из контейнера.
/// Продлевать нечего — как у пакетной аренды вне воркера
/// </summary>
internal sealed class DetachedCommandLease : ICommandLease
{
    public Task Extend(TimeSpan duration, CancellationToken ct) => Task.CompletedTask;
}
