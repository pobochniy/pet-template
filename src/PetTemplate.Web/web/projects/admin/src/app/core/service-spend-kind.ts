/**
 * Вид служебного расхода — копия `src/PetTemplate.Shared/Enums/ServiceSpendKind.cs`,
 * номера те же (спец. assistant, 13.5)
 */
export enum ServiceSpendKind {
  Compaction = 0,
  /** Расшифровка голоса — в отличие от компакции, идёт в лимит пространства */
  Transcription = 1,
  /** Сверка модели набором фраз из админки (спец. admin, 15.3) */
  ModelCheck = 2,
  /** Автооценка диалогов судьёй из админки (спец. admin, 16.3) */
  DialogEvaluation = 3
}

export const SERVICE_SPEND_TITLES: Record<ServiceSpendKind, string> = {
  [ServiceSpendKind.Compaction]: 'компакция',
  [ServiceSpendKind.Transcription]: 'расшифровка голоса',
  [ServiceSpendKind.ModelCheck]: 'сверка моделей',
  [ServiceSpendKind.DialogEvaluation]: 'автооценка диалогов'
};
