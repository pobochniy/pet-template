import { AdminInvitationStatus } from './admin-invitation-status';
import type { AdminJobKind, AdminJobStatus } from './admin-job';
import type { AssistantRunStatus, AssistantStopReason, DialogStopReason } from './assistant-run-status';
import { AdminRole } from './admin-role';
import { ProviderLedgerKind } from './provider-ledger-kind';
import { ServiceSpendKind } from './service-spend-kind';

/** `AdminDto`: кто вошёл и с какими ролями — ролями из токена */
export interface Admin {
  userId: string;
  userName: string;
  roles: AdminRole[];
}

/** `AdminLoginResultDto`: токен своей схемы и его срок — час, без продления (спец. admin, 3.3) */
export interface AdminLoginResult {
  token: string;
  expiresUtc: string;
  admin: Admin;
}

/** `AdminSummaryDto`: какая сборка API отвечает и который час на сервере (спец. admin, 6) */
export interface AdminSummary {
  version: string;
  serverTimeUtc: string;
}

/** Строка поиска аккаунтов — `AdminAccountDto` (спец. admin, 12) */
export interface AdminAccount {
  userId: string;
  userName: string;
  email: string | null;
  phoneNumber: string | null;
  hasTelegram: boolean;
  hasVk: boolean;
  registeredAtUtc: string;
}

/**
 * Пространство в карточке аккаунта — `AccountWorkspaceDto`: `isOwner` — с правом управлять
 * участниками, `isSolo` — кроме него никого. Числа своих сущностей проект дописывает сюда
 * и в `AccountCards` (точка расширения карточки)
 */
export interface AdminAccountWorkspace {
  id: string;
  name: string;
  isOwner: boolean;
  otherMembers: string[];
  isSolo: boolean;
}

/** Действие админа с аккаунтом — `AccountAdminActionKind`, подписи — `action-titles.ts` */
export interface AdminAccountAction {
  kind: number;
  adminName: string | null;
  reason: string;
  createdUtc: string;
}

/** Карточка — `AdminAccountCardDto`: по ней проверяют владельца, содержимого в ней нет */
export interface AdminAccountCard {
  card: {
    userId: string;
    userName: string;
    registeredAtUtc: string;
    hasPassword: boolean;
    hasTelegram: boolean;
    hasVk: boolean;
    canBeRemoved: boolean;
    /** `AccountActivityDto` — пустая в ядре; проект дописывает счётчики своих сущностей */
    activity: Record<string, number>;
    workspaces: AdminAccountWorkspace[];
  };
  email: string | null;
  emailConfirmed: boolean;
  phoneNumber: string | null;
  lastLoginAtUtc: string | null;
  isAdmin: boolean;
  actions: AdminAccountAction[];
}

/** Ссылка сброса: админ отправляет её человеку сам; `replaced` — выдана взамен живой, с её сроком */
export interface AdminIssuedReset {
  url: string;
  expiresAtUtc: string;
  replaced: boolean;
}

/** Освобождение почты: адрес, вписанный в чужой аккаунт без подтверждения */
export interface AdminReleaseEmail {
  email: string;
  reason: string;
}

/** Освобождение телефона: номер, вписанный в чужой аккаунт (подтверждения у телефона нет) */
export interface AdminReleasePhone {
  phone: string;
  reason: string;
}

/** Почта или телефон сняты */
export interface AdminReleasedContact {
  /** Логин аккаунта, с которого сняли адрес или номер */
  releasedFromUserName: string;
}

/** Признаки `GET Admin/Accounts/Journal`: логин (часть) или id аккаунта целиком */
export interface AdminAccountJournalFilter {
  query: string | null;
}

export interface AdminAccountJournalPage {
  entries: AdminAccountJournalEntry[];
  nextBefore: string | null;
}

/**
 * `AdminAccountJournalEntryDto`: действие админа (`kind` задан) или слияние (`kind` пуст —
 * `targetUserId` удалён, `keptUserId` остался). Логин цели — копия из журнала
 */
export interface AdminAccountJournalEntry {
  id: string;
  createdUtc: string;
  kind: number | null;
  targetUserId: string;
  targetUserName: string;
  targetExists: boolean;
  adminUserId: string | null;
  adminName: string | null;
  reason: string | null;
  keptUserId: string | null;
  keptUserName: string | null;
  platform: string | null;
}

/** `AdminMemberDto`: строка вкладки «Админы» — человек и его роли из базы */
export interface AdminMember {
  userId: string;
  userName: string;
  roles: AdminMemberRole[];
}

/** `AdminMemberRoleDto`: у первого админа и ушедшего выдавшего `grantedByName` пусто */
export interface AdminMemberRole {
  role: AdminRole;
  grantedUtc: string;
  grantedByName: string | null;
}

/** `AdminInvitationDto`: выданное приглашение без токена — в базе лежит только его хеш */
export interface AdminInvitation {
  id: string;
  roles: AdminRole[];
  status: AdminInvitationStatus;
  createdUtc: string;
  expiresUtc: string;
  createdByName: string | null;
  acceptedByName: string | null;
  revokedByName: string | null;
}

/** `AdminInvitationIssuedDto`: ответ на выдачу — ссылка видна только здесь, один раз */
export interface IssuedAdminInvitation extends AdminInvitation {
  token: string;
}

/** `AdminInvitationPreviewDto`: у отработавшего — только статус */
export interface AdminInvitationPreview {
  status: AdminInvitationStatus;
  inviterName: string | null;
  roles: AdminRole[];
}

/** Потолки расхода пространства (`AdminAssistantLimitDto`, спец. assistant, 13.6): `null` — без потолка */
export interface AdminAssistantLimit {
  tokensPerDay: number | null;
  tokensPerMonth: number | null;
  costPerDay: number | null;
  costPerMonth: number | null;
}

/** `AdminServiceSpendDto`: служебный расход одного вида — все виды, и нулевые тоже */
export interface AdminServiceSpend {
  kind: ServiceSpendKind;
  calls: number;
  tokens: number;
  cost: number;
}

/** `AdminBillingModelDto`: строка разреза по моделям */
export interface AdminBillingModel {
  modelId: string;
  displayName: string;
  provider: string;
  runs: number;
  tokens: number;
  cost: number;
  serviceTokens: number;
  serviceCost: number;
}

/** `AdminBillingWorkspaceDto`: у удалённого пространства имени нет */
export interface AdminBillingWorkspace {
  workspaceId: string;
  workspaceName: string | null;
  runs: number;
  tokens: number;
  cost: number;
  ownLimit: boolean;
}

/** `AdminLedgerFactDto`: действующий факт — последний введённый за свой период */
export interface AdminLedgerFact {
  id: string;
  periodFrom: string;
  periodTo: string;
  amount: number;
  comment: string | null;
  createdUtc: string;
  actorName: string | null;
}

/** `AdminProviderAccountDto`: наш счёт у поставщика за всё время */
export interface AdminProviderAccount {
  hasPayments: boolean;
  paid: number;
  fact: number;
  unbilled: number;
  remaining: number;
}

/** `AdminBillingProviderDto`: сверка и счёт у одного поставщика */
export interface AdminBillingProvider {
  provider: string;
  facts: AdminLedgerFact[];
  fact: number | null;
  estimate: number;
  discrepancy: number | null;
  account: AdminProviderAccount;
}

/** `AdminBillingSummaryDto`: ответ `GET Admin/Billing/Summary` (спец. admin, 14) */
export interface AdminBillingSummary {
  from: string;
  to: string;
  modelId: string | null;
  currency: string;
  runs: number;
  tokens: number;
  cost: number;
  serviceCost: number;
  service: AdminServiceSpend[];
  totalCost: number;
  defaultLimit: AdminAssistantLimit;
  models: AdminBillingModel[];
  workspaces: AdminBillingWorkspace[];
  providers: AdminBillingProvider[];
}

/** `AdminSpendUsageDto`: расход за отрезок */
export interface AdminSpendUsage {
  tokens: number;
  cost: number;
}

/** `AdminBillingUserDto`: кто сколько потратил внутри пространства; удалённый — без имени */
export interface AdminBillingUser {
  userId: string;
  name: string | null;
  runs: number;
  tokens: number;
  cost: number;
}

/** `AdminBillingWorkspaceCardDto`: лимит пространства и кто сколько потратил */
export interface AdminBillingWorkspaceCard {
  workspaceId: string;
  workspaceName: string;
  currency: string;
  ownLimit: AdminAssistantLimit | null;
  defaultLimit: AdminAssistantLimit;
  limit: AdminAssistantLimit;
  today: AdminSpendUsage;
  month: AdminSpendUsage;
  from: string;
  to: string;
  users: AdminBillingUser[];
  limitUpdatedUtc: string | null;
  limitUpdatedBy: string | null;
}

/** `AdminLedgerEntryDto`: платёж поставщику или факт расхода */
export interface AdminLedgerEntry {
  provider: string;
  kind: ProviderLedgerKind;
  amount: number;
  periodFrom: string | null;
  periodTo: string | null;
  comment: string | null;
}

/**
 * `AdminModelsDto` (спец. admin, 15.1): справочник по порядку, валюта прайса и модель
 * расшифровки голоса — она не строка справочника, но тоже стоит денег
 */
export interface AdminModels {
  /** Валюта прайса — подпись, курсов нет */
  currency: string;
  models: AdminModel[];
  /** Из конфигурации, цена за минуту; `null` — голос выключен */
  transcription: AdminTranscriptionModel | null;
}

/** `AdminTranscriptionModelDto`: правится переменной окружения, а не здесь */
export interface AdminTranscriptionModel {
  provider: string;
  model: string;
  costPerMinute: number;
}

/** `AdminModelDto`: цены по прайсу поставщика за 1000 токенов, в валюте прайса */
export interface AdminModel {
  id: string;
  provider: string;
  key: string;
  displayName: string;
  priority: number;
  costIn: number;
  costCached: number;
  costOut: number;
  isEnabled: boolean;
  /** Своя видеокарта: такому судье можно любой диалог (спец. admin, 16.3) */
  isLocal: boolean;
  /** До какого момента разомкнута размыкателем; срок в прошлом — ждёт пробного хода (спец. assistant, 16.2) */
  openUntilUtc: string | null;
  /** Отказы подряд: на пороге модель размыкается */
  failureCount: number;
  /** По модели уже ходили — поставщика и ключ не поменять (15.1) */
  isUsed: boolean;
  updatedUtc: string;
  updatedBy: string | null;
  /** Токен конкуренции (`xmin`) — едет обратно в правке (15.1) */
  version: number;
}

/**
 * `AdminModelEditDto`: порядка здесь нет — он меняется целиком отдельным методом (15.2).
 * `version` — той строки, которую правят; у новой не читается
 */
export interface AdminModelEdit {
  provider: string;
  key: string;
  displayName: string;
  costIn: number;
  costCached: number;
  costOut: number;
  isEnabled: boolean;
  /** Своя видеокарта — годится в судьи для любого диалога (16.3) */
  isLocal: boolean;
  version: number;
}

/**
 * `AdminJobDto`: сверка модели или автооценка пачки диалогов (спец. admin, 15.3 и 16.3).
 * Идёт фоном — экран перечитывает его, пока статус `Running`
 */
export interface AdminJob {
  id: string;
  kind: AdminJobKind;
  modelId: string;
  modelName: string;
  startedBy: string | null;
  createdUtc: string;
  finishedUtc: string | null;
  status: AdminJobStatus;
  planned: number;
  done: number;
  /** У оценки — диалоги, которые выбранному судье нельзя */
  skipped: number;
  /** У оценки — сорвавшиеся на поставщике */
  failed: number;
  tokensIn: number;
  tokensCached: number;
  tokensOut: number;
  /** В валюте прайса — служебный расход запуска */
  cost: number;
  error: string | null;
  /** Отчёт сверки — только у одного запуска, не в списке */
  report: ModelCheckReport | null;
}

/** `AdminEvaluationStartDto`: судья и размер пачки (спец. admin, 16.3) */
export interface AdminEvaluationStart {
  modelId: string;
  batchSize: number;
}

/** `AdminEvaluationsDto`: потолок пачки из конфигурации и последние запуски */
export interface AdminEvaluations {
  maxBatchSize: number;
  jobs: AdminJob[];
}

/** `ModelCheckReportDto`: доли попаданий и строка на фразу (спец. admin, 15.3) */
export interface ModelCheckReport {
  phrases: number;
  shares: ModelCheckShare[];
  averageSteps: number;
  results: ModelCheckPhrase[];
}

/** `ModelCheckShareDto`: `considered` — по скольким фразам признак вообще считается */
export interface ModelCheckShare {
  /** `tool`, `russian`, `clean`, `noCalls`, `failed` */
  key: string;
  hits: number;
  considered: number;
}

/** `ModelCheckPhraseDto`: признак, которого у фразы нет, — `null`, а не «попал» */
export interface ModelCheckPhrase {
  phrase: string;
  expectedTool: string | null;
  tools: string[];
  /** Обрезан: для глаз, а не для разбора */
  answer: string;
  error: string | null;
  steps: number;
  tokensIn: number;
  tokensOut: number;
  cost: number;
  toolHit: boolean;
  /** Без слов об устройстве приложения; `null` — у фразы их не задано */
  cleanAnswer: boolean | null;
  russianAnswer: boolean;
}

/**
 * Признаки `GET Admin/Dialogs` (спец. admin, 16.2): поиска по тексту нет. Дни —
 * `yyyy-MM-dd` по UTC; уточнения и оценка — из автооценки, неоценённый прогон под них
 * не подходит
 */
export interface AdminDialogFilter {
  from: string | null;
  to: string | null;
  /** Идентификатор пространства или часть названия */
  workspace: string | null;
  modelId: string | null;
  status: AssistantRunStatus | null;
  minClarifications: number | null;
  maxSatisfaction: number | null;
}

/** `AdminDialogPageDto`: `nextBefore` — курсор следующей страницы */
export interface AdminDialogPage {
  dialogs: AdminDialog[];
  nextBefore: string | null;
}

/** `AdminDialogDto`: прогон без содержимого; живёт дольше трассы */
export interface AdminDialog {
  runId: string;
  startedUtc: string;
  finishedUtc: string | null;
  workspaceId: string;
  workspaceName: string;
  modelId: string | null;
  modelName: string | null;
  status: AssistantRunStatus;
  stopReason: AssistantStopReason | null;
  steps: number;
  tokensIn: number;
  tokensOut: number;
  /** Стоимость хода по прайсу (`run_spend`); ноль — ход без токенов */
  cost: number;
  clarifications: number | null;
  satisfaction: number | null;
  traceExpired: boolean;
}

/** `AdminDialogTraceDto`: `expired` — содержимого уже нет, прогон при этом есть */
export interface AdminDialogTrace {
  dialog: AdminDialog;
  expired: boolean;
  steps: AdminDialogStep[];
  /** Автооценка прогона, если она есть (16.3) */
  evaluation: AdminDialogEvaluation | null;
}

/** `AdminDialogStepDto`: шаг трассы как есть — вход и выход усечены при записи до 4 КБ */
export interface AdminDialogStep {
  step: number;
  /** `model` или `tool` */
  kind: string;
  toolName: string | null;
  summary: string;
  input: string;
  output: string | null;
  durationMs: number;
  tokensIn: number;
  tokensOut: number;
  error: string | null;
  createdUtc: string;
}

/** `AdminDialogEvaluationDto`: что судья сказал о прогоне — только цифры и пара фраз */
export interface AdminDialogEvaluation {
  modelName: string;
  succeeded: boolean | null;
  satisfaction: number | null;
  clarifications: number | null;
  replans: number | null;
  agentMistake: boolean | null;
  stopReason: DialogStopReason | null;
  notes: string | null;
  createdUtc: string;
}

/** Признаки `GET Admin/AccessLog`: админ и период */
export interface AdminAccessLogFilter {
  adminUserId: string | null;
  from: string | null;
  to: string | null;
}

export interface AdminAccessLogPage {
  entries: AdminAccessLogEntry[];
  nextBefore: string | null;
}

/**
 * `AdminAccessLogEntryDto`: кто, когда, чей прогон; имена — пока есть у кого спросить.
 * Удалённый админ — без id и имени
 */
export interface AdminAccessLogEntry {
  id: string;
  createdUtc: string;
  adminUserId: string | null;
  adminName: string | null;
  runId: string;
  workspaceId: string;
  workspaceName: string | null;
}
