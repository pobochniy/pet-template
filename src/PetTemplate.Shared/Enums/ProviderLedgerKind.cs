namespace PetTemplate.Shared.Enums;

/// <summary>
/// Запись нашего счёта у поставщика (спец. assistant, 13.5; admin, 14). Номера уезжают
/// в базу — добавлять только в конец.
///
/// Продублировано в <c>web/projects/admin/src/app/core/provider-ledger-kind.ts</c>
/// </summary>
public enum ProviderLedgerKind
{
    /// <summary>Мы заплатили поставщику — счёт пополнился</summary>
    Payment = 0,

    /// <summary>
    /// Факт расхода за период из кабинета поставщика — то, с чем сверяется расчёт.
    /// Запись с тем же периодом позже — правка: считается последняя
    /// </summary>
    ActualSpend = 1
}
