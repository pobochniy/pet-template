import { Injectable } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { IAuthApiService } from './auth-api.service';
import { ProfileModel } from '../models/profile.model';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { UserService } from '../services/user.service';
import { TokenService } from './token.service';
import { PermissionsService } from '../services/permissions.service';
import { PermissionEnum } from '../enums/permission.enum';

@Injectable()
export class AuthApiServiceMock implements IAuthApiService {
  private mockAccessToken = 'mock_access_token_12345';

  private readonly mockUserData: ProfileModel = {
    id: '123456789',
    userName: 'MockUser',
    avatarId: 0,
    comment: 'Mock user for development',
    isAdult: true,
    hasAcceptedTerms: true,
    walletAddress: null
  };

  // Test configuration
  private shouldFail = false;
  private customMockUser: ProfileModel | null = null;

  constructor(
    private userService: UserService,
    private tokenService: TokenService,
    private permissionsService: PermissionsService
  ) {}

  setupFailure(): void {
    this.shouldFail = true;
  }

  setupSuccess(): void {
    this.shouldFail = false;
  }

  setCustomUser(user: ProfileModel): void {
    this.customMockUser = user;
  }

  async register(model: FormGroup): Promise<AuthenticationResultModel> {
    return this.telegramAuth('mock_register');
  }

  async login(model: FormGroup): Promise<AuthenticationResultModel> {
    return this.telegramAuth('mock_login');
  }

  async telegramAuth(initData: string): Promise<AuthenticationResultModel> {
    console.log('[MOCK] AuthApiService.telegramAuth called with initData:', initData);
    await new Promise(resolve => setTimeout(resolve, 500));

    if (this.shouldFail) {
      throw new Error('Mock: Telegram authentication failed');
    }

    const profile = this.customMockUser || this.mockUserData;
    const result: AuthenticationResultModel = {
      token: this.mockAccessToken,
      profile,
      permissions: [PermissionEnum.PermissionManagement]
    };

    // Сохраняем токен, профиль и permissions
    this.tokenService.Token = result.token;
    this.userService.setProfile(result.profile);
    this.permissionsService.initPermissions(result.permissions);

    console.log('[MOCK] Authentication successful. User:', profile);

    return result;
  }

  get isAuth(): boolean {
    return this.tokenService.Token !== '';
  }

  async refreshToken(): Promise<string | null> {
    console.log('[MOCK] AuthApiService.refreshToken called');
    await new Promise(resolve => setTimeout(resolve, 300));

    if (this.shouldFail) {
      throw new Error('Mock: Token refresh failed');
    }

    const newAccessToken = `mock_access_token_${Date.now()}`;
    this.tokenService.Token = newAccessToken;

    console.log('[MOCK] Token refreshed:', newAccessToken);
    return newAccessToken;
  }

  async logout(): Promise<void> {
    console.log('[MOCK] AuthApiService.logout called');
    await new Promise(resolve => setTimeout(resolve, 200));

    this.tokenService.Token = '';
    this.userService.clearProfile();
    this.permissionsService.clearPermissions();

    console.log('[MOCK] Logout successful');
  }

  // Helper methods for testing
  getMockUser(): ProfileModel {
    return this.customMockUser || this.mockUserData;
  }

  reset(): void {
    this.shouldFail = false;
    this.customMockUser = null;
    this.tokenService.Token = '';
    this.userService.clearProfile();
    this.permissionsService.clearPermissions();
  }
}
