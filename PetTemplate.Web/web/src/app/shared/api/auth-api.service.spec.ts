import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { AuthApiService } from './auth-api.service';
import { TokenService } from './token.service';
import { UserService } from '../services/user.service';
import { PermissionsService } from '../services/permissions.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { PermissionEnum } from '../enums/permission.enum';

describe('AuthApiService', () => {
  let service: AuthApiService;
  let httpMock: HttpTestingController;
  let tokenService: TokenService;
  let userService: UserService;
  let permissionsService: PermissionsService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        provideZonelessChangeDetection(),
        AuthApiService,
        TokenService,
        UserService,
        PermissionsService
      ]
    });

    service = TestBed.inject(AuthApiService);
    httpMock = TestBed.inject(HttpTestingController);
    tokenService = TestBed.inject(TokenService);
    userService = TestBed.inject(UserService);
    permissionsService = TestBed.inject(PermissionsService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('telegramAuth', () => {
    it('should authenticate user and save token, profile, and permissions', async () => {
      const initData = 'test_init_data';
      const mockResponse: AuthenticationResultModel = {
        token: 'test_token',
        profile: {
          id: '123',
          userName: 'testuser',
          avatarId: null,
          comment: null,
          isAdult: false,
          hasAcceptedTerms: false,
          walletAddress: null,
        },
        permissions: [PermissionEnum.PermissionManagement]
      };

      const promise = service.telegramAuth(initData);

      const req = httpMock.expectOne(request =>
        request.url.includes('Auth/TelegramAuth') && request.method === 'POST'
      );
      expect(req.request.body).toEqual({ initData });
      req.flush(mockResponse);

      const result = await promise;

      expect(result).toEqual(mockResponse);
      expect(tokenService.Token).toBe('test_token');
      expect(userService.profile()).toEqual(mockResponse.profile);
      expect(permissionsService.permissions()).toEqual([PermissionEnum.PermissionManagement]);
    });

    it('should handle authentication failure', async () => {
      const initData = 'invalid_data';

      const promise = service.telegramAuth(initData);

      const req = httpMock.expectOne(request =>
        request.url.includes('Auth/TelegramAuth')
      );
      req.flush({ message: 'Invalid signature' }, { status: 400, statusText: 'Bad Request' });

      await expectAsync(promise).toBeRejected();
    });
  });

  describe('isAuth', () => {
    it('should return true when token exists', () => {
      tokenService.Token = 'test_token';
      expect(service.isAuth).toBe(true);
    });

    it('should return false when token is empty', () => {
      tokenService.Token = '';
      expect(service.isAuth).toBe(false);
    });
  });

  describe('logout', () => {
    it('should clear token, profile, and permissions', async () => {
      tokenService.Token = 'test_token';
      userService.setProfile({
        id: '123',
        userName: 'testuser',
        avatarId: null,
        comment: null,
        isAdult: false,
        hasAcceptedTerms: false,
        walletAddress: null,
      });
      permissionsService.initPermissions([PermissionEnum.PermissionManagement]);

      await service.logout();

      expect(tokenService.Token).toBe('');
      expect(userService.profile()).toBeNull();
      expect(permissionsService.permissions()).toEqual([]);
    });
  });
});
