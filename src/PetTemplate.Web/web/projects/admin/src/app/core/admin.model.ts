import type { ApiSchema } from '../../../../../src/app/shared/api/generated/schema';
import type { AdminRole } from './admin-role';
import type { AssistantRunStatus } from './assistant-run-status';

/*
 * DTO админки — алиасы схем OpenAPI документа API (`npm run api:types`,
 * `src/app/shared/api/generated/schema.d.ts` продукта: документ у API один). Перечисления в
 * них — объединения номеров (`0 | 1 | 2`), с `enum` из `core/` совместимы в обе стороны, имена
 * сверяет `enum:parity`. Руками здесь — только признаки запросов (`*Filter`): их нет в схемах
 */

/**
 * Роли — `AdminRole` клиента, а не номера схемы: флаговый `AdminRole` сервера несёт `None = 0`
 * («ролей нет»), а в списке ролей приходят только настоящие, по одной (`enum:parity`, `omit`)
 */
type WithAdminRoles<T> = {
  [K in keyof T]: K extends 'roles' ? AdminRole[] : K extends 'role' ? AdminRole : T[K];
};

/** `AdminDto`: кто вошёл и с какими ролями — ролями из токена */
export type Admin = WithAdminRoles<ApiSchema<'AdminDto'>>;

/** `AdminLoginResultDto`: токен своей схемы и его срок — час, без продления (спец. admin, 3.3) */
export type AdminLoginResult = Omit<ApiSchema<'AdminLoginResultDto'>, 'admin'> & { admin: Admin };

/** `AdminSummaryDto`: какая сборка API отвечает и который час на сервере (спец. admin, 6) */
export type AdminSummary = ApiSchema<'AdminSummaryDto'>;

/** Строка поиска аккаунтов — `AdminAccountDto` (спец. admin, 12) */
export type AdminAccount = ApiSchema<'AdminAccountDto'>;

/**
 * Пространство в карточке аккаунта — `AccountWorkspaceDto`: `isOwner` — с правом управлять
 * участниками, `isSolo` — кроме него никого. Числа своих сущностей проект дописывает сюда
 * и в `AccountCards` (точка расширения карточки)
 */
export type AdminAccountWorkspace = ApiSchema<'AccountWorkspaceDto'>;

/** Действие админа с аккаунтом — `AccountAdminActionKind`, подписи — `action-titles.ts` */
export type AdminAccountAction = ApiSchema<'AdminAccountActionDto'>;

/**
 * Карточка — `AdminAccountCardDto`: по ней проверяют владельца, содержимого в ней нет.
 *
 * - `activity` — `AccountActivityDto` — пустая в ядре; проект дописывает счётчики своих сущностей
 */
export type AdminAccountCard = ApiSchema<'AdminAccountCardDto'>;

/** Ссылка сброса: админ отправляет её человеку сам; `replaced` — выдана взамен живой, с её сроком */
export type AdminIssuedReset = ApiSchema<'AdminIssuedResetDto'>;

/** Освобождение почты: адрес, вписанный в чужой аккаунт без подтверждения */
export type AdminReleaseEmail = ApiSchema<'AdminReleaseEmailDto'>;

/** Освобождение телефона: номер, вписанный в чужой аккаунт (подтверждения у телефона нет) */
export type AdminReleasePhone = ApiSchema<'AdminReleasePhoneDto'>;

/**
 * Почта или телефон сняты.
 *
 * - `releasedFromUserName` — логин аккаунта, с которого сняли адрес или номер
 */
export type AdminReleasedContact = ApiSchema<'AdminReleasedContactDto'>;

/** Признаки `GET Admin/Accounts/Journal`: логин (часть) или id аккаунта целиком */
export interface AdminAccountJournalFilter {
  query: string | null;
}

export type AdminAccountJournalPage = ApiSchema<'AdminAccountJournalPageDto'>;

/**
 * `AdminAccountJournalEntryDto`: действие админа (`kind` задан) или слияние (`kind` пуст —
 * `targetUserId` удалён, `keptUserId` остался). Логин цели — копия из журнала
 */
export type AdminAccountJournalEntry = ApiSchema<'AdminAccountJournalEntryDto'>;

/** `AdminMemberDto`: строка вкладки «Админы» — человек и его роли из базы */
export type AdminMember = Omit<ApiSchema<'AdminMemberDto'>, 'roles'> & { roles: AdminMemberRole[] };

/** `AdminMemberRoleDto`: у первого админа и ушедшего выдавшего `grantedByName` пусто */
export type AdminMemberRole = WithAdminRoles<ApiSchema<'AdminMemberRoleDto'>>;

/** `AdminInvitationDto`: выданное приглашение без токена — в базе лежит только его хеш */
export type AdminInvitation = WithAdminRoles<ApiSchema<'AdminInvitationDto'>>;

/** `AdminInvitationIssuedDto`: ответ на выдачу — ссылка видна только здесь, один раз */
export type IssuedAdminInvitation = WithAdminRoles<ApiSchema<'AdminInvitationIssuedDto'>>;

/** `AdminInvitationPreviewDto`: у отработавшего — только статус */
export type AdminInvitationPreview = WithAdminRoles<ApiSchema<'AdminInvitationPreviewDto'>>;

/** Потолки расхода пространства (`AdminAssistantLimitDto`, спец. assistant, 13.6): `null` — без потолка */
export type AdminAssistantLimit = ApiSchema<'AdminAssistantLimitDto'>;

/** `AdminServiceSpendDto`: служебный расход одного вида — все виды, и нулевые тоже */
export type AdminServiceSpend = ApiSchema<'AdminServiceSpendDto'>;

/** `AdminBillingModelDto`: строка разреза по моделям */
export type AdminBillingModel = ApiSchema<'AdminBillingModelDto'>;

/** `AdminBillingWorkspaceDto`: у удалённого пространства имени нет */
export type AdminBillingWorkspace = ApiSchema<'AdminBillingWorkspaceDto'>;

/** `AdminLedgerFactDto`: действующий факт — последний введённый за свой период */
export type AdminLedgerFact = ApiSchema<'AdminLedgerFactDto'>;

/** `AdminProviderAccountDto`: наш счёт у поставщика за всё время */
export type AdminProviderAccount = ApiSchema<'AdminProviderAccountDto'>;

/** `AdminBillingProviderDto`: сверка и счёт у одного поставщика */
export type AdminBillingProvider = ApiSchema<'AdminBillingProviderDto'>;

/** `AdminBillingSummaryDto`: ответ `GET Admin/Billing/Summary` (спец. admin, 14) */
export type AdminBillingSummary = ApiSchema<'AdminBillingSummaryDto'>;

/** `AdminSpendUsageDto`: расход за отрезок */
export type AdminSpendUsage = ApiSchema<'AdminSpendUsageDto'>;

/** `AdminBillingUserDto`: кто сколько потратил внутри пространства; удалённый — без имени */
export type AdminBillingUser = ApiSchema<'AdminBillingUserDto'>;

/** `AdminBillingWorkspaceCardDto`: лимит пространства и кто сколько потратил */
export type AdminBillingWorkspaceCard = ApiSchema<'AdminBillingWorkspaceCardDto'>;

/** `AdminLedgerEntryDto`: платёж поставщику или факт расхода */
export type AdminLedgerEntry = ApiSchema<'AdminLedgerEntryDto'>;

/**
 * `AdminModelsDto` (спец. admin, 15.1): справочник по порядку, валюта прайса и модель
 * расшифровки голоса — она не строка справочника, но тоже стоит денег.
 *
 * - `currency` — валюта прайса — подпись, курсов нет;
 * - `transcription` — из конфигурации, цена за минуту; `null` — голос выключен
 */
export type AdminModels = ApiSchema<'AdminModelsDto'>;

/** `AdminTranscriptionModelDto`: правится переменной окружения, а не здесь */
export type AdminTranscriptionModel = ApiSchema<'AdminTranscriptionModelDto'>;

/**
 * `AdminModelDto`: цены по прайсу поставщика за 1000 токенов, в валюте прайса.
 *
 * - `isLocal` — своя видеокарта: такому судье можно любой диалог (спец. admin, 16.3);
 * - `openUntilUtc` — до какого момента разомкнута размыкателем; срок в прошлом — ждёт пробного хода (спец. assistant, 16.2);
 * - `failureCount` — отказы подряд: на пороге модель размыкается;
 * - `isUsed` — по модели уже ходили — поставщика и ключ не поменять (15.1);
 * - `version` — токен конкуренции (`xmin`) — едет обратно в правке (15.1)
 */
export type AdminModel = ApiSchema<'AdminModelDto'>;

/**
 * `AdminModelEditDto`: порядка здесь нет — он меняется целиком отдельным методом (15.2).
 * `version` — той строки, которую правят; у новой не читается.
 *
 * - `isLocal` — своя видеокарта — годится в судьи для любого диалога (16.3)
 */
export type AdminModelEdit = ApiSchema<'AdminModelEditDto'>;

/**
 * `AdminJobDto`: сверка модели или автооценка пачки диалогов (спец. admin, 15.3 и 16.3).
 * Идёт фоном — экран перечитывает его, пока статус `Running`.
 *
 * - `skipped` — у оценки — диалоги, которые выбранному судье нельзя;
 * - `failed` — у оценки — сорвавшиеся на поставщике;
 * - `cost` — в валюте прайса — служебный расход запуска;
 * - `report` — отчёт сверки — только у одного запуска, не в списке
 */
export type AdminJob = ApiSchema<'AdminJobDto'>;

/** `AdminEvaluationStartDto`: судья и размер пачки (спец. admin, 16.3) */
export type AdminEvaluationStart = ApiSchema<'AdminEvaluationStartDto'>;

/** `AdminEvaluationsDto`: потолок пачки из конфигурации и последние запуски */
export type AdminEvaluations = ApiSchema<'AdminEvaluationsDto'>;

/** `ModelCheckReportDto`: доли попаданий и строка на фразу (спец. admin, 15.3) */
export type ModelCheckReport = ApiSchema<'ModelCheckReportDto'>;

/**
 * `ModelCheckShareDto`: `considered` — по скольким фразам признак вообще считается.
 *
 * - `key` — `tool`, `russian`, `clean`, `noCalls`, `failed`
 */
export type ModelCheckShare = ApiSchema<'ModelCheckShareDto'>;

/**
 * `ModelCheckPhraseDto`: признак, которого у фразы нет, — `null`, а не «попал».
 *
 * - `answer` — обрезан: для глаз, а не для разбора;
 * - `cleanAnswer` — без слов об устройстве приложения; `null` — у фразы их не задано
 */
export type ModelCheckPhrase = ApiSchema<'ModelCheckPhraseDto'>;

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
export type AdminDialogPage = ApiSchema<'AdminDialogPageDto'>;

/**
 * `AdminDialogDto`: прогон без содержимого; живёт дольше трассы.
 *
 * - `cost` — стоимость хода по прайсу (`run_spend`); ноль — ход без токенов
 */
export type AdminDialog = ApiSchema<'AdminDialogDto'>;

/**
 * `AdminDialogTraceDto`: `expired` — содержимого уже нет, прогон при этом есть.
 *
 * - `evaluation` — автооценка прогона, если она есть (16.3)
 */
export type AdminDialogTrace = ApiSchema<'AdminDialogTraceDto'>;

/**
 * `AdminDialogStepDto`: шаг трассы как есть — вход и выход усечены при записи до 4 КБ.
 *
 * - `kind` — `model` или `tool`
 */
export type AdminDialogStep = ApiSchema<'AdminDialogStepDto'>;

/** `AdminDialogEvaluationDto`: что судья сказал о прогоне — только цифры и пара фраз */
export type AdminDialogEvaluation = ApiSchema<'AdminDialogEvaluationDto'>;

/** Признаки `GET Admin/AccessLog`: админ и период */
export interface AdminAccessLogFilter {
  adminUserId: string | null;
  from: string | null;
  to: string | null;
}

export type AdminAccessLogPage = ApiSchema<'AdminAccessLogPageDto'>;

/**
 * `AdminAccessLogEntryDto`: кто, когда, чей прогон; имена — пока есть у кого спросить.
 * Удалённый админ — без id и имени
 */
export type AdminAccessLogEntry = ApiSchema<'AdminAccessLogEntryDto'>;
