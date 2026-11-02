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

/** Факт памяти — для экрана «Память ассистента» и для строки «Запомнил» в ленте (12.5) */
export interface AssistantFact {
  id: string;
  kind: AssistantFactKind;
  text: string;
  /** Личный факт собеседника, а не общий для пространства */
  personal: boolean;
  createdAtUtc: string;
  /** Ход, в котором факт записан, — из списка можно дойти до разговора */
  sourceRunId?: string | null;
}

/** Экран памяти: общее для пространства и личное — так он и сгруппирован (12.5) */
export interface AssistantMemory {
  shared: AssistantFact[];
  personal: AssistantFact[];
}

/** Операция плана: номер с единицы, вид раздела и его аргументы как есть */
export interface PlanOperation {
  index: number;
  kind: string;
  args: Record<string, unknown>;
}

export interface PlanDocument {
  operations: PlanOperation[];
}

export interface PlanOperationResult {
  index: number;
  done: boolean;
  createdId?: string | null;
  error?: string | null;
}

export interface PlanResult {
  operations: PlanOperationResult[];
}

export interface AssistantPlan {
  id: string;
  status: AssistantPlanStatus;
  operations: PlanDocument;
  /** Пусто у предложения: галочки и «Готово» — только в сообщении-отчёте (5.1) */
  result?: PlanResult | null;
  appliedAtUtc?: string | null;
}

export interface AssistantMessage {
  id: string;
  role: AssistantMessageRole;
  content: string;
  runId?: string | null;
  plan?: AssistantPlan | null;
  /** Варианты ответа на уточняющий вопрос — подсказки под сообщением (5.2) */
  options?: string[] | null;
  /**
   * Факты, записанные этим ходом: строка «Запомнил: …» с кнопкой «Убрать» (12.5).
   * Убранный факт из списка пропадает, а сообщение остаётся
   */
  facts?: AssistantFact[] | null;
  createdAtUtc: string;
}

export interface AssistantRun {
  id: string;
  status: AssistantRunStatus;
  step: number;
  stepsHard: number;
  escalated: boolean;
  /** Имя инструмента, а не текст: подпись собирает клиент словарём (раздел 3) */
  currentActivity?: string | null;
  stopReason?: AssistantStopReason | null;
  finishedAtUtc?: string | null;
  messageId?: string | null;
}

export interface AssistantHistory {
  messages: AssistantMessage[];
  activeRun?: AssistantRun | null;
  nextCursor?: string | null;
}

export interface AssistantSendResult {
  runId: string;
  messageId: string;
}

export interface AssistantTraceStep {
  step: number;
  /** `model` или `tool` */
  kind: string;
  toolName?: string | null;
  /** Строка, собранная сервером: «искал в справке: как пригласить» (13.2) */
  summary: string;
  durationMs: number;
  error?: string | null;
}

export interface AssistantTrace {
  /** Трассу вычистили по сроку — «подробности уже не хранятся» (13.1) */
  expired: boolean;
  steps: AssistantTraceStep[];
}

/** Ход закончился любым исходом — или его нет вовсе */
export function isRunFinished(run: AssistantRun | null | undefined): boolean {
  return run == null || run.status !== AssistantRunStatus.Running;
}
