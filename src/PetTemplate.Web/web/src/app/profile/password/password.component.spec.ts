import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ProfilePasswordComponent } from './password.component';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { UserService } from '../../shared/core/user.service';
import { TokenService } from '../../shared/api/token.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('ProfilePasswordComponent', () => {
  const newPassword = 'Novyi-parol-42';

  let fixture: ComponentFixture<ProfilePasswordComponent>;
  let el: HTMLElement;
  let api: AccountApiServiceMock;
  let user: UserService;
  let tokens: TokenService;

  async function setup(hasPassword: boolean, fresh = false) {
    TestBed.configureTestingModule({
      imports: [ProfilePasswordComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService }
      ]
    });

    api = TestBed.inject(AccountApiServiceMock);
    api.account = { ...api.account, hasPassword, canSetPasswordWithoutCurrent: fresh };
    user = TestBed.inject(UserService);
    tokens = TestBed.inject(TokenService);
    await loadAllScopes(TestBed.inject(I18nService));
    fixture = TestBed.createComponent(ProfilePasswordComponent);
    fixture.componentRef.setInput('hasPassword', hasPassword);
    fixture.componentRef.setInput('canSetWithoutCurrent', fresh);
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

  async function open() {
    el.querySelector<HTMLButtonElement>('#password-toggle')!.click();
    await render();
  }

  async function submit() {
    el.querySelector<HTMLButtonElement>('#password-submit')!.click();
    await render();
  }

  afterEach(() => user?.clearProfile());

  it('should ask for the current password to change it', async () => {
    await setup(true);

    await open();

    expect(el.querySelector('#password-toggle')!.textContent).toContain('Отмена');
    expect(el.querySelector('#currentPassword')).not.toBeNull();
    expect(el.querySelector('#password-submit')!.textContent).toContain('Сменить пароль');
  });

  it('should set a password without the current one for an account without a password', async () => {
    await setup(false);
    expect(el.querySelector('#password-toggle')!.textContent).toContain('Задать пароль');
    await open();
    expect(el.querySelector('#currentPassword')).toBeNull();
    type('newPassword', newPassword);
    type('newPasswordConfirm', newPassword);

    await submit();

    expect(api.password).toBe(newPassword);
    expect(el.querySelector('#password-saved')!.textContent).toContain('Пароль сохранён');
  });

  it('should replace the session with the one from the answer', async () => {
    await setup(true);
    await open();
    type('currentPassword', api.password);
    type('newPassword', newPassword);
    type('newPasswordConfirm', newPassword);

    await submit();

    expect(tokens.Token).toBe('token-1');
    expect(user.profile()?.id).toBe('user-1');
  });

  it('should show a wrong current password under its field', async () => {
    await setup(true);
    await open();
    type('currentPassword', 'Ne-tot-parol');
    type('newPassword', newPassword);
    type('newPasswordConfirm', newPassword);

    await submit();

    expect(el.querySelector('#password-form')!.textContent).toContain('Неверный текущий пароль');
    expect(tokens.Token).toBe('');
  });

  it('should not send a password shorter than eight characters', async () => {
    await setup(false);
    await open();
    type('newPassword', 'Korotki');
    type('newPasswordConfirm', 'Korotki');

    await submit();

    expect(api.account.hasPassword).toBe(false);
    expect(el.textContent).toContain('Минимум 8 символов');
  });

  /** Окна свежего входа нет — задать новый пароль без старого нельзя (8.3) */
  it('should not offer a new password without the current one outside the fresh window', async () => {
    await setup(true);

    expect(el.querySelector('#password-set-fresh')).toBeNull();
  });

  /** Свежий вход через Telegram или VK (8.3): новый пароль без старого */
  it('should set a new password without the current one after a fresh external login', async () => {
    await setup(true, true);

    el.querySelector<HTMLButtonElement>('#password-set-fresh')!.click();
    await render();
    type('newPassword', newPassword);
    type('newPasswordConfirm', newPassword);
    await submit();

    expect(el.querySelector('#currentPassword')).toBeNull();
    expect(api.password).toBe(newPassword);
    expect(el.querySelector('#password-saved')).not.toBeNull();
  });
});
