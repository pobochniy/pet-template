import type { ApiSchema } from '../api/generated/schema';

/**
 * Модели ассистента — зеркало серверных DTO (спец. assistant, 12.2.1 и раздел 14).
 *
 * Перечисления приходят числами, как и по всему проводу проекта; исключение —
 * вид операции плана (`kind`): он живёт только внутри плана, едет строкой
 * и читается глазами в разборе полётов. Видов в ядре нет — их приносят разделы
 * проекта (спец. assistant, 7.1), поэтому здесь это просто строка.
 */

/** Роль сообщения в ленте (12.2) */
export enum AssistantMessageRole {
  User = 0,
  Assistant = 1,
  /** Резюме компакции: в ленте не показывается, существует для модели (12.4) */
  Summary = 2
}

export enum AssistantPlanStatus {
  /** Собран частично: ход закончился уточняющим вопросом или «Остановить» (5.2) */
  Draft = 0,
  /** Показан человеку и ждёт согласия репликой (5.1) */
  Pending = 1,
  Applied = 2,
  Cancelled = 3
}

export enum AssistantRunStatus {
  Running = 0,
  Done = 1,
  Stopped = 2,
  Failed = 3
}

export enum AssistantStopReason {
  Completed = 0,
  StepLimit = 1,
  TokenLimit = 2,
  UserStopped = 3,
  ProviderError = 4
}

/** Вид факта памяти (12.5) — ровно три разрешённые строки «что храним» */
export enum AssistantFactKind {
  /** Псевдоним или устная договорённость */
  Alias = 0,
  /** Регулярное обстоятельство */
  Routine = 1,
  /** Умолчание, выведенное из прошлых дел */
  Default = 2
}

/*
 * Ниже — алиасы схем OpenAPI документа API (`npm run api:types`, `api/generated/schema.d.ts`).
 * Перечисления в них — объединения номеров (`0 | 1 | 2`), с `enum` выше совместимы в обе стороны
 */

/**
 * Факт памяти — для экрана «Память ассистента» и для строки «Запомнил» в ленте (12.5).
 * `personal` — личный факт собеседника, а не общий для пространства; `sourceRunId` — ход,
 * в котором факт записан: из списка можно дойти до разговора
 */
export type AssistantFact = ApiSchema<'AssistantFactDto'>;

/** Экран памяти: общее для пространства и личное — так он и сгруппирован (12.5) */
export type AssistantMemory = ApiSchema<'AssistantMemoryDto'>;

/** Операция плана: номер с единицы, вид раздела и его аргументы как есть */
export type PlanOperation = ApiSchema<'PlanOperation'>;

export type PlanDocument = ApiSchema<'PlanDocument'>;

export type PlanOperationResult = ApiSchema<'PlanOperationResult'>;

export type PlanResult = ApiSchema<'PlanResult'>;

/** `result` пуст у предложения: галочки и «Готово» — только в сообщении-отчёте (5.1) */
export type AssistantPlan = ApiSchema<'AssistantPlanDto'>;

/**
 * Сообщение ленты. `options` — варианты ответа на уточняющий вопрос, подсказки под
 * сообщением (5.2); `facts` — факты, записанные этим ходом: строка «Запомнил: …» с кнопкой
 * «Убрать» (12.5). Убранный факт из списка пропадает, а сообщение остаётся
 */
export type AssistantMessage = ApiSchema<'AssistantMessageDto'>;

/** `currentActivity` — имя инструмента, а не текст: подпись собирает клиент словарём (раздел 3) */
export type AssistantRun = ApiSchema<'AssistantRunDto'>;

export type AssistantHistory = ApiSchema<'AssistantHistoryDto'>;

export type AssistantSendResult = ApiSchema<'AssistantSendResultDto'>;

/**
 * Шаг трассы: `kind` — `model` или `tool`; `summary` — строка, собранная сервером:
 * «искал в справке: как пригласить» (13.2)
 */
export type AssistantTraceStep = ApiSchema<'AssistantTraceStepDto'>;

/** `expired` — трассу вычистили по сроку: «подробности уже не хранятся» (13.1) */
export type AssistantTrace = ApiSchema<'AssistantTraceDto'>;

/** Ход закончился любым исходом — или его нет вовсе */
export function isRunFinished(run: AssistantRun | null | undefined): boolean {
  return run == null || run.status !== AssistantRunStatus.Running;
}
