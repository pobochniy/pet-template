import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { ProfileLoginsComponent } from './logins.component';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { UserService } from '../../shared/core/user.service';
import { TokenService } from '../../shared/api/token.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { profileDto } from '../../shared/api/dto.mock';

describe('ProfileLoginsComponent', () => {
  let fixture: ComponentFixture<ProfileLoginsComponent>;
  let el: HTMLElement;
  let api: AccountApiServiceMock;
  let user: UserService;

  function configure() {
    TestBed.configureTestingModule({
      imports: [ProfileLoginsComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService }
      ]
    });

    api = TestBed.inject(AccountApiServiceMock);
    user = TestBed.inject(UserService);
    user.setProfile(profileDto({
      id: 'user-1', userName: 'tester', avatarId: 0, comment: null, isAdult: true, hasAcceptedTerms: true
    }));
  }

  async function setup(arrange: (mock: AccountApiServiceMock) => void = () => {}) {
    configure();
    arrange(api);
    await loadAllScopes(TestBed.inject(I18nService));
    fixture = TestBed.createComponent(ProfileLoginsComponent);
    el = fixture.nativeElement;
    await render();
  }

  /** Ответ подмены приходит цепочкой промисов, мимо `whenStable`: макрозадача дожидается её целиком */
  async function render() {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(id: string, value: string) {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function save() {
    el.querySelector<HTMLButtonElement>('#account-save')!.click();
    await render();
  }

  function alertTexts(): string[] {
    return TestBed.inject(AlertsService).alerts().map(x => x.content);
  }

  afterEach(() => user?.clearProfile());

  it('should show the account once the held response arrives', async () => {
    configure();
    api.hold();
    await loadAllScopes(TestBed.inject(I18nService));
    fixture = TestBed.createComponent(ProfileLoginsComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
    expect(el.querySelector('#userName')).toBeNull();

    // Без `detectChanges`: перерисовку должен запросить сам сигнал
    api.release();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();

    expect(el.querySelector<HTMLInputElement>('#userName')!.value).toBe('tester');
  });

  it('should show the main block with the login, password state and unlinked Telegram and VK', async () => {
    await setup();

    expect(el.querySelector('.card-header')!.textContent).toContain('Основное');
    expect(el.querySelector<HTMLInputElement>('#userName')!.value).toBe('tester');
    expect(el.querySelector('#password-state')!.textContent).toContain('Задан');
    expect(el.querySelector('#email-state')!.textContent).toContain('Почта подтверждена');
    expect(el.querySelector('#telegram-row')!.textContent).toContain('Не привязан');
    expect(el.querySelector('#vk-row')!.textContent).toContain('Не привязан');
  });

  it('should change the user name and take the new session', async () => {
    await setup();
    type('userName', 'Masha');

    await save();

    expect(api.account.userName).toBe('Masha');
    expect(user.profile()?.userName).toBe('Masha');
    expect(TestBed.inject(TokenService).Token).toBe('token-1');
  });

  it('should show "taken" under the user name field, not as an alert', async () => {
    await setup();
    type('userName', 'Boris');

    await save();

    expect(el.querySelector('#user-name-refusal')!.textContent).toContain('Такое имя пользователя уже занято');
    expect(alertTexts()).toEqual([]);
  });

  it('should reject a reserved user name before sending it', async () => {
    await setup();
    type('userName', 'tg123');

    await save();

    expect(el.textContent).toContain('Логины вида tg123 и vk123 заняты под автоматические');
    expect(api.account.userName).toBe('tester');
  });

  it('should save contacts without checking an unchanged automatic user name', async () => {
    await setup(mock => {
      mock.account = { ...mock.account, userName: 'tg123456', userNameIsGenerated: true };
    });
    type('phone', '+79990000009');

    await save();

    expect(api.account.phone).toBe('+79990000009');
    expect(alertTexts()).toEqual(['Профиль сохранён']);
  });

  it('should put the saved contacts into the session profile', async () => {
    await setup();
    type('phone', '+79990000009');

    await save();

    expect(user.profile()?.phone).toBe('+79990000009');
  });

  it('should show the server refusal when the email is taken', async () => {
    await setup();
    type('email', 'boris@example.com');

    await save();

    expect(alertTexts()).toEqual(['Эта почта уже зарегистрирована']);
    expect(api.account.email).toBe('tester@example.com');
  });

  it('should show the phone format rule under the field', async () => {
    await setup();
    type('phone', '8 999 000-00-01');

    await save();

    expect(el.textContent).toContain('Телефон — плюс и 11 цифр');
  });

  it('should warn an account that has only a password', async () => {
    await setup(mock => {
      mock.account = { ...mock.account, emailConfirmed: false };
    });

    expect(el.querySelector('#password-only-banner')!.textContent).toContain('Привяжите Telegram, VK или подтвердите почту');
  });

  it('should show the email as not confirmed after changing it', async () => {
    await setup();
    type('email', 'tester2@example.com');

    await save();

    expect(el.querySelector('#email-state')!.textContent).toContain('Почта не подтверждена');
  });

  it('should resend the confirmation of an unconfirmed email', async () => {
    await setup(mock => {
      mock.account = { ...mock.account, emailConfirmed: false };
    });

    el.querySelector<HTMLButtonElement>('#email-resend')!.click();
    await render();

    expect(api.confirmationsSent).toBe(1);
    expect(el.querySelector('#email-notice')!.textContent).toContain('Письмо со ссылкой отправлено');
  });

  it('should explain the mail limit', async () => {
    await setup(mock => {
      mock.account = { ...mock.account, emailConfirmed: false };
      mock.mailLimitReached = true;
    });

    el.querySelector<HTMLButtonElement>('#email-resend')!.click();
    await render();

    expect(el.querySelector('#email-error')!.textContent).toContain('Письмо уже отправлено');
  });

  /** Пока в поле не сохранённый адрес, о подтверждении сохранённого не говорим */
  it('should hide the confirmation state while the email is being edited', async () => {
    await setup();
    type('email', 'other@example.com');
    fixture.detectChanges();

    expect(el.querySelector('#email-confirmed')).toBeNull();
  });

  /** Площадки настроены — «Привязать» у непривязанных (раздел 6) */
  it('should offer linking Telegram and VK configured on the server', async () => {
    await setup();

    expect(el.querySelector('#telegram-link')).not.toBeNull();
    expect(el.querySelector('#vk-link')).not.toBeNull();
  });

  /** Отвязка — блок «Основное» перерисовывается ответом сервера */
  it('should unlink Telegram and show the new state', async () => {
    await setup(mock => mock.account = { ...mock.account, telegramLinked: true, loginCount: 2 });

    el.querySelector<HTMLButtonElement>('#telegram-unlink')!.click();
    await render();

    expect(el.querySelector('#telegram-row')!.textContent).toContain('Не привязан');
  });

  describe('confirming ownership (8.6)', () => {
    /** Окно свежего входа закрыто: сессия давняя */
    const stale = (mock: AccountApiServiceMock) => {
      mock.account = { ...mock.account, freshLoginUntilUtc: null };
    };

    it('should ask for the password before changing the email', async () => {
      await setup(stale);
      type('email', 'new@example.com');
      await save();

      expect(el.querySelector('#fresh-login')).not.toBeNull();
      expect(api.account.email).toBe('tester@example.com');

      type('fresh-password', api.password);
      el.querySelector<HTMLButtonElement>('#fresh-confirm')!.click();
      await render();
      await render();

      expect(el.querySelector('#fresh-login')).toBeNull();
      expect(api.account.email).toBe('new@example.com');
      expect(api.issuedSessions).toBeGreaterThan(0);
    });

    it('should refuse a wrong password under the field', async () => {
      await setup(stale);
      type('email', 'new@example.com');
      await save();

      type('fresh-password', 'not-the-password');
      el.querySelector<HTMLButtonElement>('#fresh-confirm')!.click();
      await render();

      expect(el.querySelector('#fresh-error')!.textContent).toContain('Неверный текущий пароль');
      expect(api.account.email).toBe('tester@example.com');
    });

    it('should not save when the confirmation is cancelled', async () => {
      await setup(stale);
      type('email', 'new@example.com');
      await save();

      el.querySelector<HTMLButtonElement>('#fresh-cancel')!.click();
      await render();

      expect(el.querySelector('#fresh-login')).toBeNull();
      expect(api.account.email).toBe('tester@example.com');
    });

    it('should save contacts without asking when the email stays', async () => {
      await setup(stale);
      type('phone', '+79990000077');
      await save();

      expect(el.querySelector('#fresh-login')).toBeNull();
      expect(api.account.phone).toBe('+79990000077');
    });

    it('should ask before unlinking', async () => {
      await setup(mock => {
        stale(mock);
        mock.account = { ...mock.account, telegramLinked: true, vkLinked: true, loginCount: 3 };
      });

      el.querySelector<HTMLButtonElement>('#vk-unlink')!.click();
      await render();
      expect(el.querySelector('#fresh-login')).not.toBeNull();

      type('fresh-password', api.password);
      el.querySelector<HTMLButtonElement>('#fresh-confirm')!.click();
      await render();
      await render();

      expect(api.account.vkLinked).toBe(false);
    });

    /** Привязка спрашивает до ухода на площадку: её код одноразовый */
    it('should ask before leaving for the platform to link', async () => {
      await setup(stale);
      const start = vi.spyOn(TestBed.inject(ExternalLoginService), 'start').mockResolvedValue(undefined);

      el.querySelector<HTMLButtonElement>('#telegram-link')!.click();
      await render();

      expect(el.querySelector('#fresh-login')).not.toBeNull();
      expect(start).not.toHaveBeenCalled();
    });

    /** Окно закрылось по дороге — сервер отказал, подтверждение спрашивается ещё раз */
    it('should ask again when the server refuses a stale window', async () => {
      await setup(mock => {
        mock.account = { ...mock.account, telegramLinked: true, vkLinked: true, loginCount: 3 };
      });
      api.account = { ...api.account, freshLoginUntilUtc: null };

      el.querySelector<HTMLButtonElement>('#vk-unlink')!.click();
      await render();

      expect(el.querySelector('#fresh-login')).not.toBeNull();
      expect(api.account.vkLinked).toBe(true);
    });

    /** Без пароля подтвердить можно только новым входом через привязанную площадку */
    it('should offer to sign in again when there is no password', async () => {
      await setup(mock => {
        stale(mock);
        mock.account = { ...mock.account, hasPassword: false, telegramLinked: true, vkLinked: true, loginCount: 2 };
      });

      el.querySelector<HTMLButtonElement>('#vk-unlink')!.click();
      await render();

      expect(el.querySelector('#fresh-password')).toBeNull();
      expect(el.querySelector('#fresh-login')!.textContent).toContain('Пароля у аккаунта нет');
      expect(el.querySelector('#fresh-telegram')).not.toBeNull();
      expect(el.querySelector('#fresh-vk')).not.toBeNull();
    });
  });
});
