import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BaseApiService } from './base-api.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import {
  AccountCardModel, AccountLoginsModel, AccountSaveModel, LinkResultModel, MergePreviewModel, MergeProofModel
} from '../models/account.model';
import { ExternalProofModel } from '../models/external-login.model';
import { ExternalProviderEnum } from '../enums/external-provider.enum';

/**
 * Свой аккаунт: логин, пароль, контакты, слияние, удаление (спец. accounts, раздел 12).
 * Заводится вместе с `account-api.service.mock.ts`
 */
export interface IAccountApiService {
  logins(): Promise<AccountLoginsModel>;

  save(model: AccountSaveModel): Promise<AccountLoginsModel>;

  changeUserName(userName: string): Promise<AuthenticationResultModel>;

  setPassword(newPassword: string, newPasswordConfirm: string): Promise<AuthenticationResultModel>;

  changePassword(currentPassword: string, newPassword: string, newPasswordConfirm: string): Promise<AuthenticationResultModel>;

  confirmPassword(password: string): Promise<AuthenticationResultModel>;

  sendEmailConfirmation(): Promise<unknown>;

  setLanguage(language: string): Promise<unknown>;

  link(proof: ExternalProofModel): Promise<LinkResultModel>;

  unlink(provider: ExternalProviderEnum): Promise<AccountLoginsModel>;

  mergePreview(proof: MergeProofModel): Promise<MergePreviewModel>;

  merge(ticket: string, keepUserId: string): Promise<AuthenticationResultModel>;

  deletePreview(): Promise<AccountCardModel>;

  deleteAccount(userName: string): Promise<unknown>;
}

@Injectable({ providedIn: 'root' })
export class AccountApiService extends BaseApiService implements IAccountApiService {
  constructor(http: HttpClient) {
    super('Account', http);
  }

  /** Блок «Основное» (раздел 6) */
  async logins() {
    return this.get<AccountLoginsModel>('Logins');
  }

  /**
   * Почта, телефон, комментарий. Занятые почта или телефон — `400 auth.error.emailTaken` /
   * `phoneTaken`, писем подтверждения сверх лимита — `400 auth.error.tooManyEmails` (8.4);
   * смена почты без свежего входа (8.6) — `400 auth.error.freshLoginRequired`; ответ — блок
   * «Основное», каким он лёг в базу
   */
  async save(model: AccountSaveModel) {
    return this.post<AccountLoginsModel>('Save', model);
  }

  /**
   * Смена логина (4.3). Занятый — `400 auth.error.userNameTaken`. Ответ — сессия с новым
   * логином: прежние токены не отзываются, а профиль сессии обновляется
   */
  async changeUserName(userName: string) {
    return this.post<AuthenticationResultModel>('ChangeUserName', { userName });
  }

  /**
   * Задать пароль (8.1). Ответ — новая сессия: отзыв завершил все прежние токены,
   * и текущий тоже — его надо заменить (`UserService.applySession`)
   */
  async setPassword(newPassword: string, newPasswordConfirm: string) {
    return this.post<AuthenticationResultModel>('SetPassword', { newPassword, newPasswordConfirm });
  }

  /**
   * Сменить пароль (8.1). Неверный текущий — `422` по полю `currentPassword`, сверх лимита
   * попыток — `400 auth.error.tooManyAttempts`. Ответ — новая сессия
   */
  async changePassword(currentPassword: string, newPassword: string, newPasswordConfirm: string) {
    return this.post<AuthenticationResultModel>('ChangePassword', { currentPassword, newPassword, newPasswordConfirm });
  }

  /**
   * Подтвердить владение паролем (8.6) — перед привязкой, отвязкой и сменой почты. Неверный —
   * `422` по полю `password`, сверх лимита попыток — `400 auth.error.tooManyAttempts`.
   * Ответ — новая сессия с отметкой входа: окно считается от неё
   */
  async confirmPassword(password: string) {
    return this.post<AuthenticationResultModel>('ConfirmPassword', { password });
  }

  /**
   * «Отправить ещё раз» письмо подтверждения (8.4). Чаще лимита —
   * `400 auth.error.tooManyEmails`, без почты — `400 auth.error.noEmail`
   */
  async sendEmailConfirmation() {
    return this.post<unknown>('SendEmailConfirmation', {});
  }

  /**
   * Язык из переключателя в шапке (8.4) — одним полем: `Save` перезаписал бы контакты
   * тем, что знает переключатель, то есть ничем
   */
  async setLanguage(language: string) {
    return this.post<unknown>('SetLanguage', { language });
  }

  /**
   * Привязать Telegram или VK (раздел 6) — тем же доказательством, что вход. Другой вход этой
   * площадки уже привязан — `400 auth.error.loginAlreadyLinked`; без свежего входа (8.6) —
   * `400 auth.error.freshLoginRequired`. Ответ — блок «Основное», а если вход принадлежит
   * другому аккаунту — вместо привязки просмотр слияния (7.1)
   */
  async link(proof: ExternalProofModel) {
    return this.post<LinkResultModel>('Link', proof);
  }

  /** Отвязать. Последний способ войти — `400 auth.error.lastLogin` (раздел 3), без свежего входа — `auth.error.freshLoginRequired` (8.6) */
  async unlink(provider: ExternalProviderEnum) {
    return this.post<AccountLoginsModel>('Unlink', { provider });
  }

  /**
   * «Объединить с другим аккаунтом» (7.1): обе карточки и ключ слияния. Без свежего входа —
   * `400 auth.error.freshLoginRequired` (8.6); неверная пара логин/пароль — `422`, вход площадки
   * ничей — `400 auth.error.mergeNoAccount`, свой же — `auth.error.mergeSameAccount`; сверх лимита
   * попыток пароля — `400 auth.error.tooManyAttempts`, у обоих роль в админке — `auth.error.adminCannotBeDeleted`
   */
  async mergePreview(proof: MergeProofModel) {
    return this.post<MergePreviewModel>('MergePreview', proof);
  }

  /**
   * Слияние (7.4). Состав пространств поменялся — `409 auth.error.mergeChanged`, ключ погашен.
   * Ответ — сессия остающегося аккаунта
   */
  async merge(ticket: string, keepUserId: string) {
    return this.post<AuthenticationResultModel>('Merge', { ticket, keepUserId });
  }

  /** Что будет при удалении (раздел 9): та же карточка, что у слияния */
  async deletePreview() {
    return this.get<AccountCardModel>('DeletePreview');
  }

  /**
   * Удалить аккаунт (раздел 9) — сразу и навсегда. Подтверждение — свой логин:
   * не он — `400 auth.error.deleteConfirmMismatch`; роль в админке — `400 auth.error.adminCannotBeDeleted`
   */
  async deleteAccount(userName: string) {
    return this.post<unknown>('Delete', { userName });
  }
}
