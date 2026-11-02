import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormGroup } from '@angular/forms';
import { BaseApiService } from './base-api.service';
import { TokenService } from './token.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { UserService } from '../core/user.service';

/**
 * Вход и регистрация. `providedIn: 'root'`, а не `serviceCollection`: оболочке (шапке)
 * нужен только выход, а его делает `UserService.logOut` — так сервис едет в чанки
 * экранов входа, а не в initial (`tech-debt/bundle.md`)
 */
@Injectable({ providedIn: 'root' })
export class AuthApiService extends BaseApiService {
  constructor(
    http: HttpClient,
    private tokenService: TokenService,
    private userService: UserService
  ) {
    super('Auth', http);
  }

  /**
   * Регистрация. С названием пространства — вместе с ним, одним сохранением
   * (спец. authorization, 3, п. 1): токен ответа уже с правами нового пространства.
   * `language` — язык интерфейса: на нём сервер пишет письма (спец. accounts, 8.4)
   */
  public async register(model: FormGroup, language: string): Promise<AuthenticationResultModel> {
    return this.applySession(await this.post<AuthenticationResultModel>('Register', { ...model.value, language }));
  }

  public async login(model: FormGroup): Promise<AuthenticationResultModel> {
    return this.applySession(await this.post<AuthenticationResultModel>('Login', model.value));
  }

  /** Проверка авторизации */
  get isAuth(): boolean {
    return this.tokenService.Token !== '';
  }

  /**
   * Выход. На сервере выходить не из чего — токен просто забывается; уборку
   * состояния разделов и сессии делает `UserService.logOut` (`SessionResetService`)
   */
  async logout(): Promise<void> {
    await this.userService.logOut();
  }

  /** Ответ любого входа раскладывается одним кодом — `UserService.applySession` */
  private applySession(result: AuthenticationResultModel): AuthenticationResultModel {
    if (result) this.userService.applySession(result);

    return result;
  }
}
