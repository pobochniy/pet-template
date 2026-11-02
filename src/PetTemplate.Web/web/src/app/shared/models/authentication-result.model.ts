import { ProfileModel } from './profile.model';
import { PermissionEnum } from '../enums/permission.enum';
import type { MergePreviewModel } from './account.model';

/** Права по пространствам: ключ — id пространства (спец. authorization, 2.1) */
export type PermissionsByWorkspace = Readonly<Record<string, PermissionEnum[]>>;

/**
 * Ответ любого входа и перевыпуска сессии — `AuthenticationResultDto` на сервере.
 * Раскладывает его один код — `UserService.applySession`
 */
export interface AuthenticationResultModel {
  token: string;
  profile: ProfileModel;
  permissionsByWorkspace: PermissionsByWorkspace;
  /**
   * Аккаунт только что заведён входом через Telegram или VK (спец. accounts, 5.4, 5.5):
   * страница возврата ведёт на «Добро пожаловать»
   */
  isNewAccount?: boolean;

  /**
   * «У меня уже есть аккаунт» в mini-app (`Auth/LogInAndLink`), а вход площадки уже
   * принадлежит другому аккаунту: привязки нет, сессия — аккаунта по паролю, а это —
   * просмотр слияния (спец. accounts, 7.1). У остальных входов нет
   */
  mergePreview?: MergePreviewModel | null;
}
