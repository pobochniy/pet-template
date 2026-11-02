import type { ExternalProofModel } from './external-login.model';

/**
 * Блок «Основное» (спец. accounts, раздел 6) — ответ `Account/Logins` и `Account/Save`.
 * Способ войти — то, чем можно войти: логин без пароля им не считается (раздел 3)
 */
export interface AccountLoginsModel {
  userName: string;

  /** Логин подставлен сервером (4.2): профиль предлагает его сменить плашкой */
  userNameIsGenerated: boolean;

  hasPassword: boolean;

  email?: string | null;

  /** Почта подтверждена (8.4) — только тогда по ней восстанавливают пароль */
  emailConfirmed: boolean;

  phone?: string | null;

  comment?: string | null;

  telegramLinked: boolean;

  vkLinked: boolean;

  /** Сколько способов войти: пароль, Telegram, VK. Последний отвязать нельзя (раздел 3) */
  loginCount: number;

  /**
   * Сессия открыта свежим входом через Telegram или VK (8.3): новый пароль задаётся без
   * старого. Решает сервер по `auth_time` токена — окно `AccountOptions.FreshLoginWindow`
   */
  canSetPasswordWithoutCurrent?: boolean;

  /**
   * До какого времени владение подтверждено свежим входом (8.6) — паролем или площадкой,
   * привязанной не только что. До него открыты привязка, отвязка, смена почты и слияние;
   * `null` — сначала подтвердить (`FreshLoginService`)
   */
  freshLoginUntilUtc?: string | null;
}

/** Тело `Account/Save`: контакты и комментарий. Пустое — «убрать» */
export interface AccountSaveModel {
  email: string | null;
  phone: string | null;
  comment: string | null;
}

/**
 * Карточка аккаунта (спец. accounts, 7.3 и 9) — `Account/DeletePreview` и половина просмотра
 * слияния: логин, способы войти, пространства с их судьбой
 */
export interface AccountCardModel {
  userId: string;
  userName: string;
  registeredAtUtc: string;
  hasPassword: boolean;
  hasTelegram: boolean;
  hasVk: boolean;

  /** Нет роли в админке: такой аккаунт можно удалить; иначе при слиянии он только остаётся (раздел 10) */
  canBeRemoved: boolean;

  /**
   * Что завёл сам человек (7.3). В ядре шаблона пусто — проект дописывает сюда счётчики
   * своих сущностей вслед за `AccountActivityDto` сервера
   */
  activity: Record<string, number>;

  workspaces: AccountWorkspaceModel[];
}

/**
 * Пространство в карточке. `isSolo` — он единственный участник: с удалением аккаунта
 * пространство удалится со всем содержимым; иначе останется остальным
 */
export interface AccountWorkspaceModel {
  id: string;
  name: string;

  /** Владелец — с правом управления участниками (7.3) */
  isOwner: boolean;
  otherMembers: string[];
  isSolo: boolean;
}

/** Просмотр слияния (7.3, 7.4): обе карточки и одноразовый ключ на 10 минут */
export interface MergePreviewModel {
  ticket: string;
  expiresAtUtc: string;

  /** Аккаунт, в котором человек сейчас */
  current: AccountCardModel;

  /** Второй — владение доказано паролем, Telegram или VK */
  other: AccountCardModel;

  /** Можно ли оставить второй: заведён не меньше суток назад (7.3); иначе остаётся только текущий */
  canKeepOther: boolean;
}

/** Доказательство владения вторым аккаунтом (7.1): логин и пароль — или вход площадки */
export interface MergeProofModel {
  login?: string;
  password?: string;
  external?: ExternalProofModel;
}

/**
 * Ответ `Account/Link`: привязан — новые «Основное»; вход чужого аккаунта — привязки нет,
 * вместо неё просмотр слияния (раздел 7)
 */
export interface LinkResultModel {
  logins?: AccountLoginsModel | null;
  mergePreview?: MergePreviewModel | null;
}
