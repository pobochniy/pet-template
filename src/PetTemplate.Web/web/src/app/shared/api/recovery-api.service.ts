import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BaseApiService } from './base-api.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';

/**
 * Восстановление доступа и подтверждение почты (спец. accounts, 8.2 и 8.4) — анонимные
 * вызовы `AuthController`. Отдельно от `AuthApiService`: тот лежит
 * в initial-бандле, а эти нужны только трём ленивым экранам — `auth/recover`,
 * `auth/reset`, `auth/confirm-email` (`tech-debt/bundle.md`)
 */
export interface IRecoveryApiService {
  requestReset(email: string): Promise<unknown>;

  resetPassword(token: string, newPassword: string, newPasswordConfirm: string): Promise<AuthenticationResultModel>;

  confirmEmail(token: string): Promise<unknown>;
}

@Injectable({ providedIn: 'root' })
export class RecoveryApiService extends BaseApiService implements IRecoveryApiService {
  constructor(http: HttpClient) {
    super('Auth', http);
  }

  /**
   * «Забыли пароль?» (8.4): ответ всегда один — есть такой аккаунт или нет; письмо
   * уходит, только если почта подтверждена
   */
  async requestReset(email: string) {
    return this.post<unknown>('RequestReset', { email });
  }

  /**
   * Новый пароль по ссылке (8.2). Ответ — новая сессия: вход сразу, раскладывает её
   * экран (`UserService.applySession`)
   */
  async resetPassword(token: string, newPassword: string, newPasswordConfirm: string) {
    return this.post<AuthenticationResultModel>('Reset', { token, newPassword, newPasswordConfirm });
  }

  /** Переход по ссылке подтверждения почты (8.4); не живая — `400 auth.error.linkInvalid` */
  async confirmEmail(token: string) {
    return this.post<unknown>('ConfirmEmail', { token });
  }
}
