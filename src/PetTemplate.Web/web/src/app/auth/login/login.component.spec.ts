import type { Mocked } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthApiService } from '../../shared/api/auth-api.service';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';
import { profileDto, sessionDto } from '../../shared/api/dto.mock';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let authService: Mocked<AuthApiService>;
  let alerts: AlertsService;
  let router: Router;

  beforeEach(async () => {
    authService = { login: vi.fn() } as unknown as Mocked<AuthApiService>;

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        provideRouter([]),
        { provide: AuthApiService, useValue: authService },
        // Язык профиля принимает `LanguageService` — ему нужен api аккаунта
        { provide: AccountApiService, useClass: AccountApiServiceMock }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    alerts = TestBed.inject(AlertsService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should render the form in the interface language', () => {
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('h4')?.textContent).toBe('Вход по логину и паролю');
    expect(el.querySelector('label[for="password"]')?.textContent).toBe('Пароль');
  });

  it('should navigate home on successful login', async () => {
    authService.login.mockResolvedValue({} as never);
    component.loginForm.setValue({ login: 'testUser', password: 'password123' });

    await component.onSubmit();

    expect(authService.login).toHaveBeenCalledWith(component.loginForm);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('should not submit an invalid form and show the field errors', async () => {
    await component.onSubmit();
    await fixture.whenStable();

    expect(authService.login).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Поле «Пароль» обязательно');
  });

  it('should show the attempts limit instead of wrong credentials', async () => {
    authService.login.mockRejectedValue(new HttpErrorResponse({
      status: 400,
      error: { code: 'auth.error.tooManyAttempts', error: 'Слишком много неудачных попыток' }
    }));
    component.loginForm.setValue({ login: 'testUser', password: 'password123' });

    await component.onSubmit();

    expect(alerts.alerts().map(a => a.content)).toEqual(['Слишком много неудачных попыток — попробуйте через 15 минут']);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('should show the wrong-credentials alert when login fails', async () => {
    authService.login.mockRejectedValue(new HttpErrorResponse({ status: 400 }));
    component.loginForm.setValue({ login: 'testUser', password: 'wrongpass' });

    await component.onSubmit();

    expect(alerts.alerts().map(a => a.content)).toEqual(['Неверный логин или пароль']);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('should translate a validation code from the 422 answer', async () => {
    authService.login.mockRejectedValue(new HttpErrorResponse({
      status: 422,
      error: { payload: [{ code: 'auth.validation.loginEqualsPassword' }] }
    }));
    component.loginForm.setValue({ login: 'samesame', password: 'samesame' });

    await component.onSubmit();

    expect(alerts.alerts().map(a => a.content)).toEqual(['Пароль должен отличаться от логина']);
  });

  it('should answer wrong credentials with its own phrase, not the server text', async () => {
    authService.login.mockRejectedValue(new HttpErrorResponse({
      status: 422,
      error: { payload: [{ code: 'auth.error.invalidCredentials', text: 'Неправильное имя пользователя или пароль' }] }
    }));
    component.loginForm.setValue({ login: 'testUser', password: 'wrongpass' });

    await component.onSubmit();

    expect(alerts.alerts().map(a => a.content)).toEqual(['Неверный логин или пароль']);
  });

  it('should offer password recovery', () => {
    const link = (fixture.nativeElement as HTMLElement).querySelector('#login-forgot') as HTMLAnchorElement;

    expect(link.getAttribute('href')).toBe('/auth/recover');
    expect(link.textContent?.trim()).toBe('Забыли пароль?');
  });

  /** Язык профиля — выбор, сделанный на другом устройстве (спец. accounts, 8.4) */
  it('should switch to the profile language after login', async () => {
    authService.login.mockResolvedValue(sessionDto({
      token: 'token',
      profile: profileDto({ id: '1', userName: 'testUser', avatarId: null, comment: null, isAdult: false, hasAcceptedTerms: false, language: 'en' }),
      permissionsByWorkspace: {}
    }));
    component.loginForm.setValue({ login: 'testUser', password: 'password123' });

    await component.onSubmit();

    expect(TestBed.inject(I18nService).lang()).toBe('en');
  });
});
