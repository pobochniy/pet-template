import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { ExternalLoginApiService } from './external-login-api.service';
import { ExternalProviderEnum } from '../enums/external-provider.enum';

/** Тела запросов входа через площадки — как их ждёт `AuthController` (спец. accounts, 5) */
describe('ExternalLoginApiService', () => {
  let service: ExternalLoginApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(ExternalLoginApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Язык интерфейса уезжает с входом: на нём сервер заведёт аккаунт и напишет письма (8.4) */
  it('should send initData of the mini-app with the interface language', async () => {
    const promise = service.telegramAuth('init-data');

    const req = http.expectOne('/api/Auth/TelegramAuth');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.initData).toBe('init-data');
    expect(req.request.body.language).toBeTruthy();
    req.flush({ token: 't', profile: {}, permissionsByWorkspace: {} });
    await promise;
  });

  it('should send VK launch params as is together with the invitation', async () => {
    const promise = service.vkAuth('vk_user_id=1&sign=x', 'token-1');

    const req = http.expectOne('/api/Auth/VkAuth');
    expect(req.request.body.launchParams).toBe('vk_user_id=1&sign=x');
    expect(req.request.body.invitation).toBe('token-1');
    req.flush({ token: 't', profile: {}, permissionsByWorkspace: {} });
    await promise;
  });

  /** Параметры площадок — один запрос на вкладку */
  it('should ask the server for external logins once', async () => {
    const first = service.externalLogins();
    const second = service.externalLogins();

    http.expectOne('/api/Auth/ExternalLogins').flush({ telegramBotId: '1', vkAppId: '' });
    expect((await first).telegramBotId).toBe('1');
    expect(await second).toEqual(await first);
  });

  /** Неудачный запрос не запоминается: следующий экран спросит снова */
  it('should ask again after a failed config request', async () => {
    const first = service.externalLogins();
    http.expectOne('/api/Auth/ExternalLogins').flush({}, { status: 500, statusText: 'Error' });
    await expectAsync(first).toBeRejected();

    const second = service.externalLogins();
    http.expectOne('/api/Auth/ExternalLogins').flush({ telegramBotId: '', vkAppId: '2' });
    expect((await second).vkAppId).toBe('2');
  });

  it('should send the proof of the fork with login and password', async () => {
    const proof = { provider: ExternalProviderEnum.Telegram, initData: 'init-data' };
    const promise = service.logInAndLink('masha', 'secret', proof);

    const req = http.expectOne('/api/Auth/LogInAndLink');
    expect(req.request.body).toEqual({ login: 'masha', password: 'secret', proof });
    req.flush({ token: 't', profile: {}, permissionsByWorkspace: {} });
    await promise;
  });
});
