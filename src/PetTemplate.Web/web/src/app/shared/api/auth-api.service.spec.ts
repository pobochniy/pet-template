import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { provideZonelessChangeDetection } from '@angular/core';
import { AuthApiService } from './auth-api.service';
import { TokenService } from './token.service';
import { UserService } from '../core/user.service';
import { PermissionsService } from '../services/permissions.service';
import { PermissionEnum } from '../enums/permission.enum';

describe('AuthApiService', () => {
  let service: AuthApiService;
  let httpMock: HttpTestingController;
  let tokenService: TokenService;
  let userService: UserService;
  let permissionsService: PermissionsService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
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
      });
      permissionsService.initPermissions([PermissionEnum.PermissionManagement]);

      await service.logout();

      expect(tokenService.Token).toBe('');
      expect(userService.profile()).toBeNull();
      expect(permissionsService.permissions()).toEqual([]);
    });
  });
});
