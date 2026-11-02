/**
 * Запись счёта у поставщика — копия `src/PetTemplate.Shared/Enums/ProviderLedgerKind.cs`,
 * номера те же (спец. admin, 14)
 */
export enum ProviderLedgerKind {
  /** Мы заплатили поставщику */
  Payment = 0,
  /** Факт расхода за период из кабинета поставщика; тот же период позже — правка */
  ActualSpend = 1
}
