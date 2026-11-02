import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { ProfileMergeStartComponent } from './merge-start.component';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { MergeStateService } from '../../shared/core/merge-state.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { FreshLoginService } from '../fresh-login/fresh-login.service';

describe('ProfileMergeStartComponent', () => {
  let fixture: ComponentFixture<ProfileMergeStartComponent>;
  let el: HTMLElement;
  let api: AccountApiServiceMock;
  let router: Router;

  async function setup(fresh = true) {
    TestBed.configureTestingModule({
      imports: [ProfileMergeStartComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService },
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock }
      ]
    });

    api = TestBed.inject(AccountApiServiceMock);
    if (!fresh) api.account = { ...api.account, freshLoginUntilUtc: null };
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    await loadAllScopes(TestBed.inject(I18nService));
    // Окно свежего входа (8.6) — из «Основное», как его кладёт блок профиля
    TestBed.inject(FreshLoginService).track(await api.logins());
    fixture = TestBed.createComponent(ProfileMergeStartComponent);
    fixture.componentRef.setInput('enabled', { telegram: true, vk: false });
    el = fixture.nativeElement;
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('#merge-start')!.click();
    fixture.detectChanges();
  }

  async function render() {
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(id: string, value: string) {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  /** Кнопки только настроенных на сервере площадок */
  it('should offer only the enabled platforms', async () => {
    await setup();

    expect(el.querySelector('#merge-by-telegram')).not.toBeNull();
    expect(el.querySelector('#merge-by-vk')).toBeNull();
  });

  it('should open the merge screen after the other password', async () => {
    await setup();
    type('merge-login', 'tg123456789');
    type('merge-password', 'Vtoroi-parol-42');

    el.querySelector<HTMLButtonElement>('#merge-by-password')!.click();
    await render();

    expect(TestBed.inject(MergeStateService).preview()?.other.userName).toBe('tg123456789');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/profile/merge');
  });

  /** Неверная пара — фраза входа, без подробностей */
  it('should refuse a wrong password like the sign-in does', async () => {
    await setup();
    type('merge-login', 'tg123456789');
    type('merge-password', 'ne-tot');

    el.querySelector<HTMLButtonElement>('#merge-by-password')!.click();
    await render();

    expect(el.querySelector('#merge-start-error')?.textContent).toContain('Неверный логин или пароль');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  /** Через площадку — тот же путь, что вход, с пометкой «слияние» */
  it('should go to the platform to prove the other account', async () => {
    await setup();
    const start = spyOn(TestBed.inject(ExternalLoginService), 'start').and.resolveTo();

    el.querySelector<HTMLButtonElement>('#merge-by-telegram')!.click();
    await render();

    expect(start).toHaveBeenCalledWith('telegram', 'merge', '/profile/merge');
  });

  /**
   * Окно свежего входа закрыто (8.6) — на площадку не уходим, пока человек не подтвердит:
   * код площадки одноразовый, и отказ после возврата потратил бы его зря
   */
  it('should ask for the fresh login before going to the platform', async () => {
    await setup(false);
    const start = spyOn(TestBed.inject(ExternalLoginService), 'start').and.resolveTo();
    const fresh = TestBed.inject(FreshLoginService);

    el.querySelector<HTMLButtonElement>('#merge-by-telegram')!.click();
    await render();

    expect(fresh.pending()).toBeTrue();
    expect(start).not.toHaveBeenCalled();

    fresh.confirmed(await api.confirmPassword(api.password).then(() => api.logins()));
    await render();

    expect(start).toHaveBeenCalledWith('telegram', 'merge', '/profile/merge');
  });

  /** Просмотр по паролю без свежего входа — ждёт подтверждения, а не уходит в отказ сервера */
  it('should wait for the fresh login before the preview', async () => {
    await setup(false);
    type('merge-login', 'tg123456789');
    type('merge-password', 'Vtoroi-parol-42');
    const fresh = TestBed.inject(FreshLoginService);

    el.querySelector<HTMLButtonElement>('#merge-by-password')!.click();
    await render();

    expect(fresh.pending()).toBeTrue();
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    fresh.confirmed(await api.confirmPassword(api.password).then(() => api.logins()));
    await render();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/profile/merge');
  });
});
