import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { WelcomeComponent } from './welcome.component';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { UserService } from '../../shared/core/user.service';
import { TokenService } from '../../shared/api/token.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { profileDto } from '../../shared/api/dto.mock';

describe('WelcomeComponent', () => {
  let fixture: ComponentFixture<WelcomeComponent>;
  let el: HTMLElement;
  let api: AccountApiServiceMock;
  let user: UserService;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [WelcomeComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService }
      ]
    });
    user = TestBed.inject(UserService);
    user.setProfile(profileDto({
      id: 'user-1', userName: 'tg12345678', userNameIsGenerated: true, avatarId: 0, comment: null,
      isAdult: false, hasAcceptedTerms: false
    }));
    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(AccountApiServiceMock);
    // Аккаунт из Telegram: логин автоматический, пароля нет, вход — один
    api.account = { ...api.account, userName: 'tg12345678', userNameIsGenerated: true, hasPassword: false, telegramLinked: true };
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fixture = TestBed.createComponent(WelcomeComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => {
    user.clearProfile();
  });

  function type(id: string, value: string) {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submit() {
    el.querySelector<HTMLButtonElement>('#welcome-submit')!.click();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('should prefill the automatic username', () => {
    expect(el.querySelector<HTMLInputElement>('#welcome-user-name')!.value).toBe('tg12345678');
  });

  /** Ничего не поменяли — ничего не сохраняем: автоматический логин правилам ввода не отвечает (4.3) */
  it('should continue without saving when nothing changed', async () => {
    await submit();

    expect(api.issuedSessions).toBe(0);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('should save the new username and the password in turn', async () => {
    type('welcome-user-name', 'masha');
    type('welcome-new-password', 'Novyi-parol-42');
    type('welcome-new-password-confirm', 'Novyi-parol-42');

    await submit();

    expect(api.account.userName).toBe('masha');
    expect(api.account.hasPassword).toBe(true);
    expect(user.profile()?.userName).toBe('masha');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('should show a taken username under the field and save nothing else', async () => {
    type('welcome-user-name', 'Boris');
    type('welcome-new-password', 'Novyi-parol-42');
    type('welcome-new-password-confirm', 'Novyi-parol-42');

    await submit();

    expect(el.querySelector('#welcome-user-name-refusal')?.textContent).toContain('уже занято');
    expect(api.account.hasPassword).toBe(false);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  /** Повторное нажатие доделывает остальное: логин уже сохранён, второй раз не уходит */
  it('should finish the rest on retry', async () => {
    type('welcome-user-name', 'masha');
    type('welcome-new-password', 'Novyi-parol-42');
    type('welcome-new-password-confirm', 'Novyi-parol-42');
    // Пароль упал на сервере после того, как логин лёг
    api.account = { ...api.account, hasPassword: true };
    await submit();
    const sessionsAfterFirst = api.issuedSessions;

    api.account = { ...api.account, hasPassword: false };
    await submit();

    expect(sessionsAfterFirst).toBe(1);
    expect(api.issuedSessions).toBe(2);
    expect(api.account.hasPassword).toBe(true);
  });

  it('should require the password twice once it is started', async () => {
    type('welcome-new-password', 'Novyi-parol-42');

    await submit();

    expect(api.account.hasPassword).toBe(false);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  /** Отказ сервера, а не поля, — плашкой */
  it('should show a server refusal as an alert', async () => {
    api.account = { ...api.account, hasPassword: true };
    type('welcome-new-password', 'Novyi-parol-42');
    type('welcome-new-password-confirm', 'Novyi-parol-42');

    await submit();

    expect(TestBed.inject(AlertsService).alerts().map(x => x.content)).toContain('Пароль уже задан — смените его, указав текущий');
  });
});
