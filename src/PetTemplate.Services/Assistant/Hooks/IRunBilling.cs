using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Assistant.Hooks;

/// <summary>
/// Учёт расхода хода (спец. assistant, 13.5): без баланса и списаний — строка расхода,
/// по которой считаются лимит пространства и сводка в админке. Цикл зовёт его при
/// завершении хода любым статусом, в той же транзакции, что и финальный статус:
/// законченный и не учтённый ход, как и учтённый и не законченный, хуже обоих вместе.
/// Реализация — <c>RunBilling</c>; <see cref="NullRunBilling"/> остаётся для тестов
/// </summary>
public interface IRunBilling
{
    /// <summary>Нужна ли транзакция под учёт — есть ли что записывать у этого хода</summary>
    bool ShouldCharge(AssistantRun run);

    /// <summary>
    /// Записать расход хода. Сохраняет вызывающий — тем же <c>SaveChanges</c>,
    /// что и финальный статус прогона
    /// </summary>
    Task Charge(AssistantRun run, CancellationToken ct);
}
