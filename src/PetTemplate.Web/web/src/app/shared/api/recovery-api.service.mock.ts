import { Injectable } from '@angular/core';
import { IRecoveryApiService } from './recovery-api.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { serverRefusal, validationRefusal } from './server-refusal.mock';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '../form-models/account-rules';
import { profileDto, sessionDto } from './dto.mock';

/**
 * Подмена `RecoveryApiService` для спек. Повторяет правила сервера (`PasswordReset`,
 * `EmailConfirmation`): ссылка срабатывает один раз, мёртвая — `400 auth.error.linkInvalid`;
 * запрос сброса отвечает одинаково на любую почту; пароль — по правилам 8.1, `422` по полю
 */
@Injectable()
export class RecoveryApiServiceMock implements IRecoveryApiService {
  /** Токены подтверждения, которые «сервер» примет — по одному разу */
  readonly confirmTokens = new Set<string>(['good-token']);

  /** Токены сброса, которые «сервер» примет — по одному разу */
  readonly resetTokens = new Set<string>(['good-token']);

  /** Какие почты просили сбросить */
  readonly resetRequests: string[] = [];

  /** Язык профиля в сессии, которую отдаёт сброс */
  language = 'ru';

  async requestReset(email: string): Promise<unknown> {
    this.resetRequests.push(email);
    return {};
  }

  async resetPassword(token: string, newPassword: string, newPasswordConfirm: string): Promise<AuthenticationResultModel> {
    if (newPassword.length < PASSWORD_MIN_LENGTH || newPassword.length > PASSWORD_MAX_LENGTH) {
      throw validationRefusal({
        newPassword: [{ code: 'core.validation.stringLength', params: { min: PASSWORD_MIN_LENGTH, max: PASSWORD_MAX_LENGTH } }]
      });
    }
    if (newPassword !== newPasswordConfirm) {
      throw validationRefusal({ newPasswordConfirm: [{ code: 'core.validation.mismatch' }] });
    }
    if (!this.resetTokens.delete(token)) {
      throw serverRefusal(400, 'Ссылка устарела или уже использована — запросите новую', 'auth.error.linkInvalid');
    }

    return sessionDto({
      token: 'reset-token',
      profile: profileDto({
        id: 'user-1', userName: 'oleg', avatarId: 0, comment: null, isAdult: false, hasAcceptedTerms: false,
        hasPassword: true, language: this.language
      }),
      permissionsByWorkspace: {}
    });
  }

  async confirmEmail(token: string): Promise<unknown> {
    if (!this.confirmTokens.delete(token)) {
      throw serverRefusal(400, 'Ссылка устарела или уже использована — запросите новую', 'auth.error.linkInvalid');
    }
    return {};
  }
}
