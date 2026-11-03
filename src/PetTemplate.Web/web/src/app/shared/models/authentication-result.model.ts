import type { ApiSchema } from '../api/generated/schema';
import { PermissionEnum } from '../enums/permission.enum';

/** Права по пространствам: ключ — id пространства (спец. authorization, 2.1) */
export type PermissionsByWorkspace = Readonly<Record<string, PermissionEnum[]>>;

/**
 * Ответ любого входа и перевыпуска сессии — `AuthenticationResultDto` на сервере (алиас схемы
 * OpenAPI, `npm run api:types`). Раскладывает его один код — `UserService.applySession`.
 *
 * - `isNewAccount` — аккаунт только что заведён входом через Telegram или VK (спец. accounts,
 *   5.4, 5.5): страница возврата ведёт на «Добро пожаловать»;
 * - `mergePreview` — «У меня уже есть аккаунт» в mini-app (`Auth/LogInAndLink`), а вход
 *   площадки уже принадлежит другому аккаунту: привязки нет, сессия — аккаунта по паролю,
 *   а это — просмотр слияния (спец. accounts, 7.1). У остальных входов `null`
 */
export type AuthenticationResultModel = ApiSchema<'AuthenticationResultDto'>;
