import { Injectable } from '@angular/core';
import { IExternalLoginApiService } from './external-login-api.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { ExternalCodeModel, ExternalLoginsConfigModel, ExternalProofModel } from '../models/external-login.model';
import { MergePreviewModel } from '../models/account.model';
import { serverRefusal, validationRefusal } from './server-refusal.mock';
import { profileDto, sessionDto } from './dto.mock';

/**
 * Подмена `ExternalLoginApiService` для спек. Повторяет правила сервера (`AuthController`):
 * площадка не подтвердила вход — `400 auth.error.externalFailed`; с `existingOnly`
 * неизвестный вход — `404 auth.error.loginNotLinked`, аккаунт не заводится; неизвестный
 * без него — новый аккаунт с `isNewAccount`. Mini-app: неизвестный вход без приглашения —
 * `404 auth.error.unknownLogin`; «У меня уже есть аккаунт» с неверной парой — `422`, а если
 * вход уже у другого аккаунта (`linkBelongsToOther`) — сессия по паролю и просмотр слияния
 */
@Injectable()
export class ExternalLoginApiServiceMock implements IExternalLoginApiService {
  config: ExternalLoginsConfigModel = {
    telegramBotId: '123456',
    telegramAuthorizationEndpoint: 'https://oauth.telegram.org/auth',
    telegramRedirectUri: 'https://example.com/auth/telegram',
    vkAppId: '5555555',
    vkAuthorizationEndpoint: 'https://id.vk.ru/authorize',
    vkRedirectUri: 'https://example.com/auth/vk'
  };

  /** Сервер не ответил на `Auth/ExternalLogins` */
  configFails = false;

  /** Коды, которые площадка «подтвердит» */
  validCodes = new Set<string>(['good-code']);

  /** Вход уже привязан к аккаунту — иначе неизвестный */
  known = false;

  /** Mini-app открыт по приглашению — неизвестный вход заводит аккаунт сразу */
  withInvitation = false;

  /** Вход площадки уже у другого аккаунта: «У меня уже есть аккаунт» ведёт в слияние (7.1) */
  linkBelongsToOther: MergePreviewModel | null = null;

  /** Пароль аккаунта для «У меня уже есть аккаунт» */
  password = 'Staryi-parol-42';

  /** Что уходило на вход последним */
  lastRequest: ExternalCodeModel | null = null;
  lastProof: ExternalProofModel | null = null;
  lastInvitation: string | null = null;

  async externalLogins(): Promise<ExternalLoginsConfigModel> {
    if (this.configFails) throw serverRefusal(500, 'Внутренняя ошибка сервера');
    return { ...this.config };
  }

  async telegramLogin(code: ExternalCodeModel): Promise<AuthenticationResultModel> {
    return this.signIn(code, 'tg12345678');
  }

  async vkId(code: ExternalCodeModel): Promise<AuthenticationResultModel> {
    return this.signIn(code, 'vk2308481');
  }

  async telegramAuth(initData: string): Promise<AuthenticationResultModel> {
    if (!initData) throw serverRefusal(400, 'Invalid or stale Telegram signature');
    return this.launch('tg12345678', this.withInvitation);
  }

  async vkAuth(launchParams: string, invitation?: string | null): Promise<AuthenticationResultModel> {
    this.lastInvitation = invitation ?? null;
    if (!launchParams) throw serverRefusal(400, 'Sign is invalid');
    return this.launch('vk2308481', this.withInvitation || !!invitation);
  }

  async registerExternal(proof: ExternalProofModel): Promise<AuthenticationResultModel> {
    this.lastProof = proof;
    this.known = true;
    return this.session('tg12345678', true);
  }

  async logInAndLink(login: string, password: string, proof: ExternalProofModel): Promise<AuthenticationResultModel> {
    this.lastProof = proof;
    if (password !== this.password) {
      throw validationRefusal({ payload: [{ code: 'auth.error.invalidCredentials' }] });
    }
    const session = this.session(login, false);
    return this.linkBelongsToOther ? { ...session, mergePreview: this.linkBelongsToOther } : session;
  }

  private launch(automaticUserName: string, invited: boolean): AuthenticationResultModel {
    if (!this.known && !invited) {
      throw serverRefusal(404, 'Этот вход ещё не знаком', 'auth.error.unknownLogin');
    }
    return this.session(this.known ? 'masha' : automaticUserName, !this.known);
  }

  private signIn(code: ExternalCodeModel, automaticUserName: string): AuthenticationResultModel {
    this.lastRequest = code;
    if (!this.validCodes.has(code.code ?? '')) {
      throw serverRefusal(400, 'Не удалось войти через Telegram или VK. Попробуйте ещё раз', 'auth.error.externalFailed');
    }
    if (code.existingOnly && !this.known) {
      throw serverRefusal(404, 'Этот Telegram или VK не привязан ни к одному аккаунту', 'auth.error.loginNotLinked');
    }

    return this.session(this.known ? 'masha' : automaticUserName, !this.known);
  }

  private session(userName: string, isNewAccount: boolean): AuthenticationResultModel {
    return sessionDto({
      token: 'external-token',
      profile: profileDto({
        id: 'user-1',
        userName,
        userNameIsGenerated: isNewAccount,
        hasPassword: !isNewAccount,
        avatarId: 0,
        comment: null,
        language: 'ru',
        isAdult: false,
        hasAcceptedTerms: false,
        primaryWorkspaceId: null
      }),
      permissionsByWorkspace: {},
      isNewAccount
    });
  }
}
