import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { BaseApiService } from './base-api.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { ExternalCodeModel, ExternalLoginsConfigModel, ExternalProofModel } from '../models/external-login.model';
import { I18nService } from '../i18n/i18n.service';

/**
 * Вход через Telegram и VK (спец. accounts, 5): параметры площадок, вход с сайта и вход
 * из mini-app. Отдельно от `AuthApiService`, как в ASU: экраны входа через площадки
 * ленивые, и их код уезжает в свои чанки (`tech-debt/bundle.md`). Сессию раскладывает
 * экран — `UserService.applySession`. Заводится вместе с `external-login-api.service.mock.ts`.
 *
 * Новый аккаунт заводится на языке интерфейса — на нём уйдут письма (8.4): язык уезжает
 * с каждым входом, сервер пишет его только при заведении
 */
export interface IExternalLoginApiService {
  externalLogins(): Promise<ExternalLoginsConfigModel>;

  telegramLogin(code: ExternalCodeModel): Promise<AuthenticationResultModel>;

  vkId(code: ExternalCodeModel): Promise<AuthenticationResultModel>;

  telegramAuth(initData: string): Promise<AuthenticationResultModel>;

  vkAuth(launchParams: string, invitation?: string | null): Promise<AuthenticationResultModel>;

  registerExternal(proof: ExternalProofModel): Promise<AuthenticationResultModel>;

  logInAndLink(login: string, password: string, proof: ExternalProofModel): Promise<AuthenticationResultModel>;
}

@Injectable({ providedIn: 'root' })
export class ExternalLoginApiService extends BaseApiService implements IExternalLoginApiService {
  private readonly i18n = inject(I18nService);

  /**
   * Параметры площадок не меняются, пока сервер не перевыкатили: один запрос на вкладку.
   * Неудачный не запоминается — следующий экран спросит снова
   */
  private config?: Promise<ExternalLoginsConfigModel>;

  constructor(http: HttpClient) {
    super('Auth', http);
  }

  /** Публичные параметры площадок (5.1): пустой id — площадка выключена, кнопки нет */
  externalLogins(): Promise<ExternalLoginsConfigModel> {
    this.config ??= this.get<ExternalLoginsConfigModel>('ExternalLogins').catch(error => {
      this.config = undefined;
      throw error;
    });
    return this.config;
  }

  /** Вход и регистрация через Telegram (5.2): `id_token` окна входа сервер проверяет сам */
  telegramLogin(code: ExternalCodeModel): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('TelegramLogin', { ...code, language: this.i18n.lang() });
  }

  /** Вход и регистрация через VK ID (5.3): код и PKCE-verifier меняет на пользователя сервер */
  vkId(code: ExternalCodeModel): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('VkId', { ...code, language: this.i18n.lang() });
  }

  /**
   * Вход из mini-app Telegram по подписанному `initData` (5.6). Неизвестный Telegram без
   * приглашения — `404 auth.error.unknownLogin`: аккаунт не заводится, экран показывает развилку
   */
  telegramAuth(initData: string): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('TelegramAuth', { initData, language: this.i18n.lang() });
  }

  /**
   * Вход из mini-app VK по параметрам запуска строкой, как есть (5.6). Приглашение из
   * фрагмента VK не подписывает — сервер верит только живому приглашению с этим токеном
   */
  vkAuth(launchParams: string, invitation?: string | null): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('VkAuth', {
      launchParams,
      invitation: invitation ?? null,
      language: this.i18n.lang()
    });
  }

  /** Развилка mini-app, «Начать» (5.6): завести аккаунт тем же подписанным входом */
  registerExternal(proof: ExternalProofModel): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('RegisterExternal', { proof, language: this.i18n.lang() });
  }

  /**
   * Развилка mini-app, «У меня уже есть аккаунт» (5.6): войти логином и паролем и привязать
   * этот Telegram (VK) одним вызовом. Неверная пара — `422`, как у входа; вход уже у другого
   * аккаунта — привязки нет, в ответе сессия по паролю и `mergePreview` (спец. accounts, 7.1)
   */
  logInAndLink(login: string, password: string, proof: ExternalProofModel): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('LogInAndLink', { login, password, proof });
  }
}
