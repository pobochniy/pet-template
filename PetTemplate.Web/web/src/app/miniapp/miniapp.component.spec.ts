import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideZonelessChangeDetection } from '@angular/core';
import { MiniappComponent } from './miniapp.component';
import { AuthApiService } from '../shared/api/auth-api.service';
import { AlertsService } from '../shared/alerts/alerts.service';
import { signal } from '@angular/core';
import { PermissionEnum } from '../shared/enums/permission.enum';

describe('MiniappComponent', () => {
  let component: MiniappComponent;
  let fixture: ComponentFixture<MiniappComponent>;
  let mockAuthService: jasmine.SpyObj<AuthApiService>;
  let mockRouter: jasmine.SpyObj<Router>;
  let mockAlertsService: jasmine.SpyObj<AlertsService>;

  beforeEach(async () => {
    mockAuthService = jasmine.createSpyObj('AuthApiService', ['telegramAuth']);
    Object.defineProperty(mockAuthService, 'isAuth', {
      get: jasmine.createSpy('isAuth').and.returnValue(false),
      configurable: true
    });
    mockRouter = jasmine.createSpyObj('Router', ['navigateByUrl']);
    mockAlertsService = jasmine.createSpyObj('AlertsService', ['push'], {
      alerts: signal([])
    });

    await TestBed.configureTestingModule({
      imports: [MiniappComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: AuthApiService, useValue: mockAuthService },
        { provide: Router, useValue: mockRouter },
        { provide: AlertsService, useValue: mockAlertsService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(MiniappComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('ngOnInit', () => {
    beforeEach(() => {
      // Очищаем window.Telegram перед каждым тестом
      (window as any).Telegram = undefined;
    });

    it('should show error when Telegram initData is not available', async () => {
      await component.ngOnInit();

      expect(mockAlertsService.push).toHaveBeenCalledWith('danger', 'Ошибка при подключении к Telegram');
      expect(component.isLoading()).toBe(false);
    });

    it('should authenticate successfully and redirect to home', async () => {
      // Подготовка mock Telegram данных
      (window as any).Telegram = {
        WebApp: {
          initData: 'user=%7B%22id%22%3A123%7D&auth_date=1234567890&hash=test_hash'
        }
      };

      mockAuthService.telegramAuth.and.returnValue(Promise.resolve({
        token: 'test_token',
        profile: {
          id: '123',
          userName: 'TestUser',
          avatarId: 0,
          comment: '',
          isAdult: true,
          hasAcceptedTerms: true,
          walletAddress: null,
        },
        permissions: []
      }));

      (Object.getOwnPropertyDescriptor(mockAuthService, 'isAuth')?.get as jasmine.Spy).and.returnValue(true);
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      await component.ngOnInit();

      expect(mockAuthService.telegramAuth).toHaveBeenCalledWith('user=%7B%22id%22%3A123%7D&auth_date=1234567890&hash=test_hash');
      expect(mockRouter.navigateByUrl).toHaveBeenCalledWith('/');
      expect(component.isLoading()).toBe(false);
    });

    it('should show error when authentication fails', async () => {
      (window as any).Telegram = {
        WebApp: {
          initData: 'invalid_data'
        }
      };

      mockAuthService.telegramAuth.and.returnValue(Promise.resolve({
        token: '',
        profile: null as any,
        permissions: []
      }));

      (Object.getOwnPropertyDescriptor(mockAuthService, 'isAuth')?.get as jasmine.Spy).and.returnValue(false);

      await component.ngOnInit();

      expect(mockAlertsService.push).toHaveBeenCalledWith('danger', 'Ошибка аутентификации');
      expect(mockRouter.navigateByUrl).not.toHaveBeenCalled();
      expect(component.isLoading()).toBe(false);
    });

    it('should handle authentication service error', async () => {
      (window as any).Telegram = {
        WebApp: {
          initData: 'test_data'
        }
      };

      mockAuthService.telegramAuth.and.returnValue(Promise.reject(new Error('Network error')));

      await component.ngOnInit();

      expect(mockAlertsService.push).toHaveBeenCalledWith('danger', 'Ошибка при подключении к Telegram');
      expect(component.isLoading()).toBe(false);
    });

    it('should set isLoading to false after completion', async () => {
      (window as any).Telegram = {
        WebApp: {
          initData: 'test_data'
        }
      };

      mockAuthService.telegramAuth.and.returnValue(Promise.resolve({
        token: 'test_token',
        profile: {} as any,
        permissions: []
      }));

      (Object.getOwnPropertyDescriptor(mockAuthService, 'isAuth')?.get as jasmine.Spy).and.returnValue(true);
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      expect(component.isLoading()).toBe(true);

      await component.ngOnInit();

      expect(component.isLoading()).toBe(false);
    });

    it('should authenticate with permissions and redirect', async () => {
      (window as any).Telegram = {
        WebApp: {
          initData: 'user=%7B%22id%22%3A123%7D&auth_date=1234567890&hash=test_hash'
        }
      };

      const mockPermissions = [PermissionEnum.PermissionManagement];
      mockAuthService.telegramAuth.and.returnValue(Promise.resolve({
        token: 'test_token',
        profile: {
          id: '123',
          userName: 'TestUser',
          avatarId: 0,
          comment: '',
          isAdult: true,
          hasAcceptedTerms: true,
          walletAddress: null,
        },
        permissions: mockPermissions
      }));

      (Object.getOwnPropertyDescriptor(mockAuthService, 'isAuth')?.get as jasmine.Spy).and.returnValue(true);
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      await component.ngOnInit();

      expect(mockAuthService.telegramAuth).toHaveBeenCalledWith('user=%7B%22id%22%3A123%7D&auth_date=1234567890&hash=test_hash');
      expect(mockRouter.navigateByUrl).toHaveBeenCalledWith('/');
      expect(component.isLoading()).toBe(false);
    });
  });
});
