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

/** Публичные параметры площадок с сервера (`Auth/ExternalLogins`). Пустой id — вход выключен */
export interface ExternalLoginsConfigModel {
  /** Id бота — `client_id` окна входа Telegram; начало токена до двоеточия, не секрет */
  telegramBotId: string;
  telegramAuthorizationEndpoint: string;
  telegramRedirectUri: string;
  vkAppId: string;
  vkAuthorizationEndpoint: string;
  vkRedirectUri: string;
}

/**
 * Что вернула площадка (`Auth/TelegramLogin`, `Auth/VkId`): Telegram — `id_token` окна
 * входа в `code`, VK ID — код и PKCE-verifier
 */
export interface ExternalCodeModel {
  code: string;
  verifier?: string | null;
  deviceId?: string | null;
  /** `state` входа: VK ждёт его при обмене кода, у Telegram это `nonce` внутри `id_token` */
  state?: string | null;
  /**
   * Только войти в существующий аккаунт (восстановление доступа, 8.3): неизвестный вход —
   * `404 auth.error.loginNotLinked`, аккаунт не заводится
   */
  existingOnly?: boolean;
}

/**
 * Доказательство владения входом площадки — `ExternalProofDto` сервера (спец. accounts, 6):
 * на сайте — то, что вернула площадка (`code` и прочее), в mini-app — подписанные
 * параметры запуска (`initData` Telegram, `vkLaunchParams` VK). Одно на привязку
 * (`Account/Link`), слияние (`Account/MergePreview`) и развилку mini-app
 * (`Auth/RegisterExternal`, `Auth/LogInAndLink`)
 */
export interface ExternalProofModel {
  provider: ExternalProviderEnum;
  code?: string | null;
  verifier?: string | null;
  deviceId?: string | null;
  state?: string | null;
  initData?: string | null;
  vkLaunchParams?: string | null;
}
