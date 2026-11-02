import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { ExternalCallbackComponent, afterSignIn } from './external-callback.component';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { TokenService } from '../../shared/api/token.service';
import { EXTERNAL_LOGIN_STORAGE_KEY, PendingExternalLogin } from '../../shared/core/external-login.service';
import { SESSION_USER_KEY, UserService } from '../../shared/core/user.service';
import { MergeStateService } from '../../shared/core/merge-state.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { ExternalProviderEnum } from '../../shared/enums/external-provider.enum';

describe('ExternalCallbackComponent', () => {
  let api: ExternalLoginApiServiceMock;
  let account: AccountApiServiceMock;
  let router: Router;

  async function render(query: Record<string, string>, provider = 'telegram', signedIn = false,
                        arrange: () => void = () => {}): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [ExternalCallbackComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        { provide: ExternalLoginApiServiceMock, useExisting: ExternalLoginApiService },
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { data: { provider }, queryParamMap: convertToParamMap(query) } }
        }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    const user = TestBed.inject(UserService);
    TestBed.inject(TokenService).Token = '';
    if (signedIn) {
      user.setProfile({ id: 'user-1', userName: 'tester', avatarId: 0, comment: null, isAdult: true, hasAcceptedTerms: true });
    } else {
      user.clearProfile();
    }
    api = TestBed.inject(ExternalLoginApiServiceMock);
    account = TestBed.inject(AccountApiServiceMock);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    arrange();

    const fixture = TestBed.createComponent(ExternalCallbackComponent);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement;
  }

  function remember(pending: Partial<PendingExternalLogin> = {}): void {
    sessionStorage.setItem(EXTERNAL_LOGIN_STORAGE_KEY, JSON.stringify({
      provider: 'telegram', purpose: 'login', state: 'st-1', verifier: 'ver-1', returnUrl: '/', ...pending
    }));
  }

  afterEach(() => {
    sessionStorage.removeItem(EXTERNAL_LOGIN_STORAGE_KEY);
    // Сессию вход кладёт в `localStorage` — следующей спеке она не достаётся
    localStorage.removeItem(SESSION_USER_KEY);
    localStorage.removeItem('token');
  });

  it('should exchange the VK code with the remembered verifier', async () => {
    remember({ provider: 'vk' });
    await render({ code: 'good-code', state: 'st-1', device_id: 'dev-1' }, 'vk', false, () => api.known = true);

    expect(api.lastRequest).toEqual(jasmine.objectContaining({ code: 'good-code', verifier: 'ver-1', deviceId: 'dev-1' }));
    expect(router.navigateByUrl).toHaveBeenCalledWith('/', { replaceUrl: true });
  });

  it('should send the Telegram token with the state it was issued for', async () => {
    remember({ idToken: 'good-code' });
    await render({ state: 'st-1' }, 'telegram', false, () => api.known = true);

    expect(api.lastRequest).toEqual(jasmine.objectContaining({ code: 'good-code', state: 'st-1', verifier: null }));
    expect(TestBed.inject(TokenService).Token).toBe('external-token');
  });

  /** Аккаунт только что заведён — «Добро пожаловать» (5.4) */
  it('should open the welcome screen for a new account', async () => {
    remember({ idToken: 'good-code' });

    await render({ state: 'st-1' });

    expect(router.navigateByUrl).toHaveBeenCalledWith('/welcome', { replaceUrl: true });
  });

  /** Начали с приглашения — туда и возвращаемся: лендинг предложит его принять */
  it('should return a new account to the invitation it started from', () => {
    expect(afterSignIn(true, '/invite/abc')).toBe('/invite/abc');
    expect(afterSignIn(false, '/workspace/members')).toBe('/workspace/members');
  });

  /** Восстановление доступа (8.3): только в существующий аккаунт */
  it('should sign in only to an existing account when recovering', async () => {
    remember({ idToken: 'good-code', purpose: 'recover' });

    const el = await render({ state: 'st-1' });

    expect(api.lastRequest?.existingOnly).toBeTrue();
    expect(el.querySelector('#external-error')?.textContent).toContain('не привязан ни к одному аккаунту');
    expect(el.querySelector('#external-back')?.getAttribute('href')).toBe('/auth/login');
  });

  it('should refuse a return with a foreign state', async () => {
    remember({ provider: 'vk' });

    const el = await render({ code: 'good-code', state: 'st-2' }, 'vk');

    expect(api.lastRequest).toBeNull();
    expect(el.querySelector('#external-error')).not.toBeNull();
  });

  /** Человек отказался в окне Telegram — вход отменён, на сервер ничего не уходит */
  it('should report a cancelled Telegram sign-in', async () => {
    remember({ idToken: null });

    const el = await render({ state: 'st-1' });

    expect(api.lastRequest).toBeNull();
    expect(el.querySelector('#external-error')?.textContent).toContain('Вход отменён');
  });

  it('should show the refusal when the platform did not confirm the sign-in', async () => {
    remember({ idToken: 'stale-code' });

    const el = await render({ state: 'st-1' });

    expect(el.querySelector('#external-error')?.textContent).toContain('Не удалось войти через Telegram или VK');
  });

  /** Привязка из профиля (раздел 6): тот же код уходит в `Account/Link`, и человек возвращается в профиль */
  it('should link the login to the signed-in account', async () => {
    remember({ provider: 'vk', purpose: 'link', returnUrl: '/profile/edit' });

    await render({ code: 'good-code', state: 'st-1', device_id: 'dev-1' }, 'vk', true);

    expect(account.account.vkLinked).toBeTrue();
    expect(account.linkedProof).toEqual(jasmine.objectContaining({ provider: ExternalProviderEnum.Vk, code: 'good-code' }));
    expect(api.lastRequest).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/profile/edit', { replaceUrl: true });
  });

  /** Вход чужого аккаунта — привязки нет, сервер отдал просмотр слияния (7.1): на экран слияния */
  it('should open the merge screen when the login belongs to another account', async () => {
    remember({ provider: 'vk', purpose: 'link', returnUrl: '/profile/edit' });

    await render({ code: 'good-code', state: 'st-1', device_id: 'dev-1' }, 'vk', true,
      () => account.takenExternal.add(ExternalProviderEnum.Vk));

    expect(account.account.vkLinked).toBeFalse();
    expect(TestBed.inject(MergeStateService).preview()?.ticket).toBe('ticket-1');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/profile/merge', { replaceUrl: true });
  });

  /** «Объединить с другим аккаунтом» через площадку (7.1): вход уходит в `MergePreview` */
  it('should prove the other account for merging', async () => {
    remember({ provider: 'telegram', purpose: 'merge', returnUrl: '/profile/merge', idToken: 'good-code' });

    await render({ state: 'st-1' }, 'telegram', true);

    expect(api.lastRequest).toBeNull();
    expect(TestBed.inject(MergeStateService).preview()?.other.userName).toBe('tg123456789');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/profile/merge', { replaceUrl: true });
  });

  /** Площадка не подтвердила вход при слиянии — отказ, кнопка ведёт обратно в профиль */
  it('should refuse a merge proof the platform did not confirm', async () => {
    remember({ provider: 'telegram', purpose: 'merge', returnUrl: '/profile/merge', idToken: 'forged' });

    const el = await render({ state: 'st-1' }, 'telegram', true);

    expect(el.querySelector('#external-error')?.textContent).toContain('Не удалось войти через Telegram или VK');
    expect(el.querySelector('#external-back')?.getAttribute('href')).toBe('/profile/edit');
  });
});
