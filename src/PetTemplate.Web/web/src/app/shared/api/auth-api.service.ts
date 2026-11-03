import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormGroup } from '@angular/forms';
import { BaseApiService } from './base-api.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';

/**
 * Вход и регистрация. `providedIn: 'root'`, а не `serviceCollection`: оболочке (шапке)
 * нужен только выход, а его делает `UserService.logOut` — так сервис едет в чанки
 * экранов входа, а не в initial (`tech-debt/bundle.md`).
 *
 * Только запросы: ответ — DTO, сессию раскладывает экран через `UserService.applySession`
 */
@Injectable({ providedIn: 'root' })
export class AuthApiService extends BaseApiService {
  constructor(http: HttpClient) {
    super('Auth', http);
  }

  /**
   * Регистрация. С названием пространства — вместе с ним, одним сохранением
   * (спец. authorization, 3, п. 1): токен ответа уже с правами нового пространства.
   * `language` — язык интерфейса: на нём сервер пишет письма (спец. accounts, 8.4)
   */
  public register(model: FormGroup, language: string): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('Register', { ...model.value, language });
  }

  public login(model: FormGroup): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('Login', model.value);
  }
}
