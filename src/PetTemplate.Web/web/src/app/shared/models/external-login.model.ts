import type { ApiSchema } from '../api/generated/schema';
import { ExternalProviderEnum } from '../enums/external-provider.enum';

/** Площадки, через которые входят на сайте и в mini-app (спец. accounts, 5.1, 5.6) */
export type ExternalProvider = 'telegram' | 'vk';

/**
 * Зачем человека отправили на площадку: войти (или завести аккаунт), привязать этот вход
 * к уже открытому аккаунту из блока «Основное» профиля (спец. accounts, 6),
 * восстановить доступ через привязанный вход (8.2, 8.3) — войти только в существующий
 * аккаунт, не заводя нового, — или доказать владение вторым аккаунтом для слияния (7.1)
 */
export type ExternalPurpose = 'login' | 'link' | 'recover' | 'merge';

/** Площадка на проводе — числом, как `ExternalProvider` сервера */
export const EXTERNAL_PROVIDER_CODES: Record<ExternalProvider, ExternalProviderEnum> = {
  telegram: ExternalProviderEnum.Telegram,
  vk: ExternalProviderEnum.Vk
};

/*
 * Ниже — алиасы схем OpenAPI документа API (`npm run api:types`, `api/generated/schema.d.ts`);
 * выше — типы клиента, которых на проводе нет
 */

/**
 * Публичные параметры площадок с сервера (`Auth/ExternalLogins`). Пустой id — вход выключен.
 * `telegramBotId` — `client_id` окна входа Telegram: начало токена до двоеточия, не секрет
 */
export type ExternalLoginsConfigModel = ApiSchema<'ExternalLoginsConfigDto'>;

/**
 * Что вернула площадка (`Auth/TelegramLogin`, `Auth/VkId`): Telegram — `id_token` окна
 * входа в `code`, VK ID — код и PKCE-verifier.
 *
 * - `state` — `state` входа: VK ждёт его при обмене кода, у Telegram это `nonce` внутри `id_token`;
 * - `existingOnly` — только войти в существующий аккаунт (восстановление доступа, 8.3):
 *   неизвестный вход — `404 auth.error.loginNotLinked`, аккаунт не заводится
 */
export type ExternalCodeModel = ApiSchema<'ExternalCodeDto'>;

/**
 * Доказательство владения входом площадки — `ExternalProofDto` сервера (спец. accounts, 6):
 * на сайте — то, что вернула площадка (`code` и прочее), в mini-app — подписанные
 * параметры запуска (`initData` Telegram, `vkLaunchParams` VK). Одно на привязку
 * (`Account/Link`), слияние (`Account/MergePreview`) и развилку mini-app
 * (`Auth/RegisterExternal`, `Auth/LogInAndLink`)
 */
export type ExternalProofModel = ApiSchema<'ExternalProofDto'>;
