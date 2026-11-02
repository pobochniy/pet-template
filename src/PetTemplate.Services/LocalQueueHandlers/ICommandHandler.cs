namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Обработчик команды очереди — та же сигнатура, что у <c>LocalQueue.Processing.ICommandHandler&lt;T&gt;</c>
/// пакета <c>localqueue</c> (Lad, ASU). Свой интерфейс — потому что пакета очереди под
/// PostgreSQL пока нет и команды разбирает внутренний <c>CommandQueueWorker</c>
/// (<c>tech-debt/localqueue-postgres.md</c>); с пакетом поменяется только <c>using</c>.
///
/// Исключение — обработка не удалась: строка остаётся в очереди и будет повторена.
/// Повтор обязан быть безопасным — обработчик сам решает, что уже сделано
/// </summary>
public interface ICommandHandler<in TCommand>
{
    Task Handle(TCommand command, CancellationToken ct);
}
