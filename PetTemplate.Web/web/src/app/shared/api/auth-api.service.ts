import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BaseApiService } from './base-api.service';
import { TokenService } from './token.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { UserService } from '../services/user.service';
import { PermissionsService } from '../services/permissions.service';
import {FormGroup} from '@angular/forms';

export interface IAuthApiService {
  register(model: FormGroup): Promise<AuthenticationResultModel>;
  login(model: FormGroup): Promise<AuthenticationResultModel>;
  telegramAuth(initData: string): Promise<AuthenticationResultModel>;
  refreshToken(): Promise<string | null>;
  logout(): Promise<void>;
  get isAuth(): boolean;
}

@Injectable()
export class AuthApiService extends BaseApiService implements IAuthApiService {
  constructor(
    http: HttpClient,
    tokenService: TokenService,
    private userService: UserService,
    private permissionsService: PermissionsService
  ) {
    super('Auth', tokenService, http);
  }

  public async register(model: FormGroup): Promise<AuthenticationResultModel> {
    const result = await this.post<AuthenticationResultModel>('Register', model.value);
    if (result) {
      this.tokenService.Token = result.token;
      this.userService.setProfile(result.profile);
      this.permissionsService.initPermissions(result.permissions);
    }
    return result;
  }

  public async login(model: FormGroup): Promise<AuthenticationResultModel> {
    const result = await this.post<AuthenticationResultModel>('Login', model.value);
    if (result) {
      this.tokenService.Token = result.token;
      this.userService.setProfile(result.profile);
      this.permissionsService.initPermissions(result.permissions);
    }
    return result;
  }

  /** Аутентификация через Telegram Mini App */
  async telegramAuth(initData: string): Promise<AuthenticationResultModel> {
    console.log('[AuthApiService] telegramAuth called');
    const result = await this.post<AuthenticationResultModel>('TelegramAuth', { initData });

    console.log('[AuthApiService] telegramAuth result:', result);

    if (result) {
      // Сохраняем токен
      this.tokenService.Token = result.token;
      console.log('[AuthApiService] Token saved:', result.token.substring(0, 20) + '...');

      // Сохраняем профиль в UserService
      this.userService.setProfile(result.profile);

      // Сохраняем permissions
      this.permissionsService.initPermissions(result.permissions);
    }

    return result;
  }

  /** Проверка авторизации */
  get isAuth(): boolean {
    return this.tokenService.Token !== '';
  }

  /** Обновление токена */
  async refreshToken(): Promise<string | null> {
    const refreshToken = localStorage.getItem('refreshToken');
    if (!refreshToken) return null;

    try {
      const res = await this.post<{ accessToken: string; refreshToken: string }>('Refresh', {
        refreshToken
      });

      if (res) {
        this.tokenService.Token = res.accessToken;
        localStorage.setItem('refreshToken', res.refreshToken);
        return res.accessToken;
      }
    } catch (error) {
      this.logout();
      throw error;
    }

    return null;
  }

  /** Выход */
  async logout(): Promise<void> {
    this.tokenService.Token = '';
    this.userService.clearProfile();
    this.permissionsService.clearPermissions();
  }
}
