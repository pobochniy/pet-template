import type { AssistantHistory, AssistantMessage, AssistantPlan, AssistantRun } from '../models/assistant.model';
import { AssistantMessageRole, AssistantPlanStatus, AssistantRunStatus } from '../models/assistant.model';
import type { AuthenticationResultModel } from '../models/authentication-result.model';
import type { ProfileModel } from '../models/profile.model';

/*
 * Ответы сервера для спек — целиком, как их описывает OpenAPI документ API (модели —
 * алиасы его схем, `npm run api:types`): обязательное поле, которое спека не задала,
 * получает здесь правдоподобное значение, а спека задаёт только то, что проверяет.
 * Появилось у DTO новое обязательное поле — дописывается сюда, а не в каждую спеку
 */

/** Профиль из ответа входа и `Profile/Details` */
export function profileDto(fields: Partial<ProfileModel> = {}): ProfileModel {
  return {
    id: 'user-1',
    userName: null,
    phone: null,
    email: null,
    avatarId: null,
    comment: null,
    primaryWorkspaceId: null,
    // Флаги — ложью, поля — `null`: так спека, которая их не задала, видела их и до алиасов (`undefined`)
    userNameIsGenerated: false,
    hasPassword: false,
    emailConfirmed: false,
    // Язык по умолчанию клиента: `LanguageService.adopt` после входа ничего не переключит
    language: 'ru',
    isAdult: true,
    hasAcceptedTerms: true,
    ...fields
  };
}

/** Ответ входа и перевыпуска сессии */
export function sessionDto(fields: Partial<AuthenticationResultModel> = {}): AuthenticationResultModel {
  return {
    token: 'token',
    profile: profileDto(),
    permissionsByWorkspace: {},
    isNewAccount: false,
    mergePreview: null,
    ...fields
  };
}

/** Сообщение ленты ассистента */
export function assistantMessageDto(fields: Partial<AssistantMessage> = {}): AssistantMessage {
  return {
    id: 'message-1',
    role: AssistantMessageRole.User,
    content: '',
    runId: null,
    plan: null,
    options: null,
    facts: [],
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...fields
  };
}

/** Прогон ассистента */
export function assistantRunDto(fields: Partial<AssistantRun> = {}): AssistantRun {
  return {
    id: 'run-1',
    status: AssistantRunStatus.Running,
    step: 0,
    stepsHard: 8,
    escalated: false,
    currentActivity: null,
    stopReason: null,
    finishedAtUtc: null,
    messageId: null,
    ...fields
  };
}

/** Лента ассистента: страница сообщений без идущего хода и следующей страницы */
export function assistantHistoryDto(fields: Partial<AssistantHistory> = {}): AssistantHistory {
  return { messages: [], activeRun: null, nextCursor: null, ...fields };
}

/** План ассистента: предложение без отчёта */
export function assistantPlanDto(fields: Partial<AssistantPlan> = {}): AssistantPlan {
  return {
    id: 'plan-1',
    status: AssistantPlanStatus.Pending,
    operations: { operations: [] },
    result: null,
    appliedAtUtc: null,
    ...fields
  };
}
