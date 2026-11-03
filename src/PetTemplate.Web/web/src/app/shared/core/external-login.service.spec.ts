import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { EXTERNAL_LOGIN_STORAGE_KEY, ExternalLoginService, PendingExternalLogin } from './external-login.service';
import { ExternalLoginApiService } from '../api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../api/external-login-api.service.mock';

describe('ExternalLoginService', () => {
  let service: ExternalLoginService;
  let api: ExternalLoginApiServiceMock;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        { provide: ExternalLoginApiServiceMock, useExisting: ExternalLoginApiService }
      ]
    });
    service = TestBed.inject(ExternalLoginService);
    api = TestBed.inject(ExternalLoginApiServiceMock);
  });

  afterEach(() => sessionStorage.removeItem(EXTERNAL_LOGIN_STORAGE_KEY));

  function remembered(): PendingExternalLogin {
    return JSON.parse(sessionStorage.getItem(EXTERNAL_LOGIN_STORAGE_KEY)!) as PendingExternalLogin;
  }

  it('should enable only the platforms the server configured', async () => {
    api.config.vkAppId = '';

    expect(await service.enabled()).toEqual({ telegram: true, vk: false });
  });

  /** Сервер не ответил — кнопок нет, а не упавшая страница входа */
  it('should enable nothing when the server does not answer', async () => {
    api.configFails = true;

    expect(await service.enabled()).toEqual({ telegram: false, vk: false });
  });

  it('should send VK the PKCE challenge and remember the verifier and state', async () => {
    let url = '';
    vi.spyOn(service, 'redirect').mockImplementation(x => url = x);

    await service.start('vk', 'login', '/invite/abc');

    const pending = remembered();
    const params = new URL(url).searchParams;
    expect(url.startsWith('https://id.vk.ru/authorize?')).toBe(true);
    expect(params.get('client_id')).toBe('5555555');
    expect(params.get('state')).toBe(pending.state);
    expect(params.get('code_challenge_method')).toBe('S256');
    expect(params.get('code_challenge')).not.toBe(pending.verifier);
    expect(pending.returnUrl).toBe('/invite/abc');
  });

  /** Открытый редирект через `returnUrl` уводил бы с сайта */
  it('should keep the return address on this site', async () => {
    vi.spyOn(service, 'redirect').mockImplementation(() => {});

    await service.start('vk', 'login', '//evil.example/');

    expect(remembered().returnUrl).toBe('/');
  });

  it('should hand over the Telegram token from the sign-in window with its state as nonce', async () => {
    const popup = fakePopup();
    vi.spyOn(service, 'openPopup').mockReturnValue(popup as unknown as Window);
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    const started = service.start('telegram', 'link', '/profile/edit');
    await new Promise(resolve => setTimeout(resolve));
    const nonce = new URL(popup.location.href).searchParams.get('nonce');
    window.dispatchEvent(new MessageEvent('message', {
      origin: 'https://oauth.telegram.org',
      source: popup as unknown as Window,
      data: JSON.stringify({ event: 'auth_result', result: 'id-token' })
    }));
    await started;

    const pending = remembered();
    expect(nonce).toBe(pending.state);
    expect(pending.idToken).toBe('id-token');
    expect(pending.purpose).toBe('link');
    expect(router.navigateByUrl).toHaveBeenCalledWith(`/auth/telegram?state=${encodeURIComponent(pending.state)}`);
  });

  it('should take the remembered login once and only with the same state', () => {
    sessionStorage.setItem(EXTERNAL_LOGIN_STORAGE_KEY, JSON.stringify({
      provider: 'vk', purpose: 'login', state: 'st-1', verifier: 'v', returnUrl: '/'
    }));

    expect(service.take('vk', 'st-1')?.verifier).toBe('v');
    expect(service.take('vk', 'st-1')).toBeNull();
  });

  it('should refuse a return with another state', () => {
    sessionStorage.setItem(EXTERNAL_LOGIN_STORAGE_KEY, JSON.stringify({
      provider: 'vk', purpose: 'login', state: 'st-1', verifier: 'v', returnUrl: '/'
    }));

    expect(service.take('vk', 'st-2')).toBeNull();
  });
});

/**
 * Окно входа Telegram без настоящего окна: адрес и признак «закрыто». Основа — `MessagePort`:
 * источником `MessageEvent` браузер принимает только окно или порт
 */
function fakePopup() {
  const popup = Object.assign(new MessageChannel().port1, { closed: false, location: { href: '' } });
  popup.close = () => {
    popup.closed = true;
  };
  return popup;
}
