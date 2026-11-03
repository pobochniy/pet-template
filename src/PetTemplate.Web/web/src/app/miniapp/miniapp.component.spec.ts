import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { MiniappComponent } from './miniapp.component';
import { ExternalLoginApiService } from '../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../shared/api/external-login-api.service.mock';
import { TokenService } from '../shared/api/token.service';
import { AccountApiService } from '../shared/api/account-api.service';
import { AccountApiServiceMock } from '../shared/api/account-api.service.mock';
import { AlertsService } from '../shared/alerts/alerts.service';
import { UserService } from '../shared/core/user.service';
import { MergeStateService } from '../shared/core/merge-state.service';
import { LAUNCH_STORAGE_PREFIX } from '../shared/core/launch-params';
import { ExternalProviderEnum } from '../shared/enums/external-provider.enum';
import { I18nService } from '../shared/i18n/i18n.service';
import { loadAllScopes } from '../shared/i18n/i18n.testing';

/** Подписанный `initData`, как его снял скрипт `index.html` из `#tgWebAppData` */
const INIT_DATA = 'user=%7B%22id%22%3A123%7D&auth_date=1234567890&hash=test_hash';

/** Параметры запуска VK — строкой, как VK дописал их к адресу `/vk` */
const VK_LAUNCH = 'vk_app_id=51797414&vk_ts=1234567890&vk_user_id=2308481&sign=test_sign';

const INVITATION = '01234567-89ab-cdef-0123-456789abcdef';

describe('MiniappComponent', () => {
  let fixture: ComponentFixture<MiniappComponent>;
  let el: HTMLElement;
  let api: ExternalLoginApiServiceMock;
  let router: Router;

  async function render(provider: 'telegram' | 'vk', arrange: (mock: ExternalLoginApiServiceMock) => void = () => {}) {
    TestBed.configureTestingModule({
      imports: [MiniappComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        { provide: ExternalLoginApiServiceMock, useExisting: ExternalLoginApiService },
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: ActivatedRoute, useValue: { snapshot: { data: { provider } } } }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(ExternalLoginApiServiceMock);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    arrange(api);

    fixture = TestBed.createComponent(MiniappComponent);
    el = fixture.nativeElement;
    await settle();
  }

  async function settle() {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function click(id: string) {
    el.querySelector<HTMLButtonElement>(`#${id}`)!.click();
    await settle();
  }

  function launch(name: 'tg' | 'vk' | 'vkHash', value: string) {
    sessionStorage.setItem(LAUNCH_STORAGE_PREFIX + name, value);
  }

  afterEach(() => {
    for (const name of ['tg', 'vk', 'vkHash']) sessionStorage.removeItem(LAUNCH_STORAGE_PREFIX + name);
    TestBed.inject(UserService).clearProfile();
  });

  it('should explain on the screen that the app is opened outside Telegram', async () => {
    await render('telegram');

    expect(TestBed.inject(AlertsService).alerts().map(x => x.content)).toContain('Не удалось подключиться к Telegram');
    expect(fixture.componentInstance.isLoading()).toBe(false);
  });

  /** Известный Telegram — вход и главная */
  it('should sign in a known Telegram and go home', async () => {
    launch('tg', INIT_DATA);
    await render('telegram', mock => mock.known = true);

    expect(TestBed.inject(TokenService).Token).toBe('external-token');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  /** Неизвестный вход без приглашения — развилка, а не молчаливое заведение аккаунта (5.6) */
  it('should offer the fork for an unknown Telegram', async () => {
    launch('tg', INIT_DATA);
    await render('telegram');

    expect(el.querySelector('#miniapp-fork')).not.toBeNull();
    expect(TestBed.inject(TokenService).Token).toBe('');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  /** «Начать» — тот же подписанный вход заводит аккаунт, дальше «Добро пожаловать» */
  it('should register with the same proof on start and open welcome', async () => {
    launch('tg', INIT_DATA);
    await render('telegram');

    await click('miniapp-start');

    expect(api.lastProof).toEqual({ provider: ExternalProviderEnum.Telegram, initData: INIT_DATA });
    expect(TestBed.inject(TokenService).Token).toBe('external-token');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/welcome');
  });

  /** «У меня уже есть аккаунт» — логин и пароль привязывают этот Telegram */
  it('should log in and link the Telegram to an existing account', async () => {
    launch('tg', INIT_DATA);
    await render('telegram');
    await click('miniapp-have-account');

    const form = fixture.componentInstance.loginForm;
    form.setValue({ login: 'masha', password: 'Staryi-parol-42' });
    await click('miniapp-login-submit');

    expect(api.lastProof?.initData).toBe(INIT_DATA);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  /**
   * «У меня уже есть аккаунт», а этот Telegram успел войти в другой аккаунт: сессия по паролю
   * и экран слияния (спец. accounts, 7.1) вместо отказа
   */
  it('should open the merge screen when the Telegram belongs to another account', async () => {
    launch('tg', INIT_DATA);
    await render('telegram', mock => mock.linkBelongsToOther = new AccountApiServiceMock().preview);
    await click('miniapp-have-account');

    fixture.componentInstance.loginForm.setValue({ login: 'masha', password: 'Staryi-parol-42' });
    await click('miniapp-login-submit');

    expect(TestBed.inject(TokenService).Token).toBe('external-token');
    expect(TestBed.inject(MergeStateService).preview()?.ticket).toBe('ticket-1');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/profile/merge');
  });

  it('should keep the fork with its own phrase on a wrong password', async () => {
    launch('tg', INIT_DATA);
    await render('telegram');
    await click('miniapp-have-account');

    fixture.componentInstance.loginForm.setValue({ login: 'masha', password: 'ne-tot' });
    await click('miniapp-login-submit');

    expect(TestBed.inject(AlertsService).alerts().map(x => x.content)).toContain('Неверный логин или пароль');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  /** VK: параметры запуска строкой как есть и приглашение из фрагмента; после входа — лендинг приглашения */
  it('should sign in from VK with the invitation of the fragment', async () => {
    launch('vk', VK_LAUNCH);
    launch('vkHash', `inv_${INVITATION.replace(/-/g, '')}`);
    await render('vk');

    expect(api.lastInvitation).toBe(INVITATION);
    expect(router.navigateByUrl).toHaveBeenCalledWith(`/invite/${INVITATION}`);
  });

  /** Сервер недоступен — объяснение на экране, а не вечный лоадер */
  it('should explain the failure on the screen when the server is unreachable', async () => {
    launch('tg', INIT_DATA);
    vi.spyOn(console, 'error').mockImplementation(() => {});
    await render('telegram', mock => vi.spyOn(mock, 'telegramAuth').mockRejectedValue(new Error('Network error')));

    expect(el.querySelector('h3')?.textContent).toBe('Ошибка входа');
  });
});
