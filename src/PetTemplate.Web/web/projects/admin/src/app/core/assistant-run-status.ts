/** Состояние прогона — копия `src/PetTemplate.Shared/Enums/AssistantRunStatus.cs`, номера те же */
export enum AssistantRunStatus {
  Running = 0,
  Done = 1,
  Stopped = 2,
  Failed = 3
}

export const RUN_STATUSES: AssistantRunStatus[] = [
  AssistantRunStatus.Running,
  AssistantRunStatus.Done,
  AssistantRunStatus.Stopped,
  AssistantRunStatus.Failed
];

export const RUN_STATUS_TITLES: Record<AssistantRunStatus, string> = {
  [AssistantRunStatus.Running]: 'идёт',
  [AssistantRunStatus.Done]: 'готово',
  [AssistantRunStatus.Stopped]: 'остановлен',
  [AssistantRunStatus.Failed]: 'сорвался'
};

/** Чем закончился ход — копия `src/PetTemplate.Shared/Enums/AssistantStopReason.cs` */
export enum AssistantStopReason {
  Completed = 0,
  StepLimit = 1,
  TokenLimit = 2,
  UserStopped = 3,
  ProviderError = 4
}

export const STOP_REASON_TITLES: Record<AssistantStopReason, string> = {
  [AssistantStopReason.Completed]: 'дошёл до ответа',
  [AssistantStopReason.StepLimit]: 'лимит шагов',
  [AssistantStopReason.TokenLimit]: 'лимит токенов',
  [AssistantStopReason.UserStopped]: 'остановлен человеком',
  [AssistantStopReason.ProviderError]: 'отказ поставщика'
};

/**
 * Куда упёрся диалог по мнению судьи автооценки — копия
 * `src/PetTemplate.Shared/Enums/DialogStopReason.cs` (спец. admin, 16.3)
 */
export enum DialogStopReason {
  StepLimit = 0,
  Repetition = 1,
  ProviderError = 2,
  Other = 3
}

export const DIALOG_STOP_REASON_TITLES: Record<DialogStopReason, string> = {
  [DialogStopReason.StepLimit]: 'лимит шагов или токенов',
  [DialogStopReason.Repetition]: 'звал одно и то же по кругу',
  [DialogStopReason.ProviderError]: 'отказ поставщика',
  [DialogStopReason.Other]: 'другое'
};
