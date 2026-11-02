using PetTemplate.Services.Admin;
using PetTemplate.Services.Commands;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Сверка модели (спец. admin, 15.3) — см. <see cref="ModelCheckService"/>. Идёт минутами:
/// два десятка ходов, — поэтому аренда команды продлевается перед каждой фразой и каждым
/// шагом, как у хода, иначе второй цикл подхватил бы её посреди работы.
///
/// Дорожка — своя, админская (<c>CommandQueueLanes.Admin</c>): ход людей за сверкой
/// не стоит и сверка за ходами — тоже
/// </summary>
public class ModelCheckCommandHandler(ModelCheckService checks, ICommandLease lease)
    : ICommandHandler<ModelCheckCommand>
{
    /// <summary>
    /// На сколько продлевается аренда перед фразой и шагом — как у хода: шаг на
    /// медленной модели — минуты, а не секунды
    /// </summary>
    public static readonly TimeSpan Lease = AssistantRunCommandHandler.Lease;

    public async Task Handle(ModelCheckCommand command, CancellationToken ct) =>
        await checks.Run(command.JobId, () => lease.Extend(Lease, ct), ct);
}
