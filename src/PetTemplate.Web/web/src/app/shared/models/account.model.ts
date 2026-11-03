import type { ApiSchema } from '../api/generated/schema';

/*
 * Модели — алиасы схем OpenAPI документа API (`npm run api:types`, `api/generated/schema.d.ts`):
 * поля, их `null` и обязательность — с сервера, а не копией руками
 */

/**
 * Блок «Основное» (спец. accounts, раздел 6) — ответ `Account/Logins` и `Account/Save`.
 * Способ войти — то, чем можно войти: логин без пароля им не считается (раздел 3).
 *
 * - `userNameIsGenerated` — логин подставлен сервером (4.2): профиль предлагает его сменить плашкой;
 * - `emailConfirmed` — почта подтверждена (8.4): только тогда по ней восстанавливают пароль;
 * - `loginCount` — сколько способов войти: пароль, Telegram, VK. Последний отвязать нельзя (раздел 3);
 * - `canSetPasswordWithoutCurrent` — сессия открыта свежим входом через Telegram или VK (8.3):
 *   новый пароль задаётся без старого. Решает сервер по `auth_time` токена —
 *   окно `AccountOptions.FreshLoginWindow`;
 * - `freshLoginUntilUtc` — до какого времени владение подтверждено свежим входом (8.6) —
 *   паролем или площадкой, привязанной не только что. До него открыты привязка, отвязка,
 *   смена почты и слияние; `null` — сначала подтвердить (`FreshLoginService`)
 */
export type AccountLoginsModel = ApiSchema<'AccountLoginsDto'>;

/** Тело `Account/Save`: контакты и комментарий. Пустое — «убрать» */
export type AccountSaveModel = ApiSchema<'AccountSaveDto'>;

/**
 * Карточка аккаунта (спец. accounts, 7.3 и 9) — `Account/DeletePreview` и половина просмотра
 * слияния: логин, способы войти, пространства с их судьбой.
 *
 * - `canBeRemoved` — нет роли в админке: такой аккаунт можно удалить; иначе при слиянии
 *   он только остаётся (раздел 10);
 * - `activity` — что завёл сам человек (7.3). В ядре пусто — проект дописывает счётчики
 *   своих сущностей в `AccountActivityDto` сервера, и они приходят сюда сами
 */
export type AccountCardModel = ApiSchema<'AccountCardDto'>;

/**
 * Пространство в карточке. `isSolo` — он единственный участник: с удалением аккаунта
 * пространство удалится со всем содержимым; иначе останется остальным. `isOwner` —
 * владелец, с правом управления участниками (7.3)
 */
export type AccountWorkspaceModel = ApiSchema<'AccountWorkspaceDto'>;

/**
 * Просмотр слияния (7.3, 7.4): обе карточки и одноразовый ключ на 10 минут. `current` —
 * аккаунт, в котором человек сейчас; `other` — второй, владение доказано паролем, Telegram
 * или VK; `canKeepOther` — его можно оставить: заведён не меньше суток назад (7.3),
 * иначе остаётся только текущий
 */
export type MergePreviewModel = ApiSchema<'MergePreviewDto'>;

/** Доказательство владения вторым аккаунтом (7.1): логин и пароль — или вход площадки */
export type MergeProofModel = ApiSchema<'MergeProofDto'>;

/**
 * Ответ `Account/Link`: привязан — новые «Основное»; вход чужого аккаунта — привязки нет,
 * вместо неё просмотр слияния (раздел 7)
 */
export type LinkResultModel = ApiSchema<'LinkResultDto'>;
