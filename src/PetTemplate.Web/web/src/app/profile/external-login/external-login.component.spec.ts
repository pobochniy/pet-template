import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { ProfileExternalLoginComponent } from './external-login.component';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { FreshLoginService } from '../fresh-login/fresh-login.service';
import { ProfileFreshLoginComponent } from '../fresh-login/fresh-login.component';
import { AccountLoginsModel } from '../../shared/models/account.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('ProfileExternalLoginComponent', () => {
  let fixture: ComponentFixture<ProfileExternalLoginComponent>;
  let el: HTMLElement;
  let api: AccountApiServiceMock;
  let changed: AccountLoginsModel[];

  async function render(logins: Partial<AccountLoginsModel>, enabled = true) {
    TestBed.configureTestingModule({
      imports: [ProfileExternalLoginComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService },
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(AccountApiServiceMock);
    api.account = { ...api.account, ...logins };
    // Владение подтверждено (8.6) — подтверждение проверяет спека «Основное»
    TestBed.inject(FreshLoginService).track(api.account);

    fixture = TestBed.createComponent(ProfileExternalLoginComponent);
    fixture.componentRef.setInput('provider', 'telegram');
    fixture.componentRef.setInput('logins', api.account);
    fixture.componentRef.setInput('enabled', enabled);
    changed = [];
    fixture.componentInstance.changed.subscribe(x => changed.push(x));
    el = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function click(id: string) {
    el.querySelector<HTMLButtonElement>(`#${id}`)!.click();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('should offer to link a free login through the platform', async () => {
    await render({ telegramLinked: false });
    const start = vi.spyOn(TestBed.inject(ExternalLoginService), 'start').mockResolvedValue(undefined);

    await click('telegram-link');

    expect(start).toHaveBeenCalledWith('telegram', 'link', '/profile/edit', undefined);
  });

  /** Площадка не настроена на сервере — привязать некуда */
  it('should not offer linking when the platform is off', async () => {
    await render({ telegramLinked: false }, false);

    expect(el.querySelector('#telegram-link')).toBeNull();
  });

  it('should unlink when another way to sign in remains', async () => {
    await render({ telegramLinked: true, hasPassword: true, loginCount: 2 });

    await click('telegram-unlink');

    expect(changed[0].telegramLinked).toBe(false);
  });

  /** Последний способ войти не отвязывается (раздел 3): кнопка выключена, подсказка видна */
  it('should not unlink the last way to sign in', async () => {
    await render({ telegramLinked: true, hasPassword: false, loginCount: 1 });

    expect(el.querySelector<HTMLButtonElement>('#telegram-unlink')!.disabled).toBe(true);
    expect(el.querySelector('#telegram-last-hint')?.textContent).toContain('Сначала задайте пароль');
  });
});

/**
 * Окно Telegram при закрытом окне свежего входа (спец. accounts, 8.6): браузер пускает
 * `window.open` только в ответ на клик, поэтому окно открывается в самом клике
 * «Подтвердить» — до запросов `ConfirmPassword` и `Logins`, — а ведётся на Telegram после них
 */
@Component({
  imports: [ProfileExternalLoginComponent, ProfileFreshLoginComponent],
  template: `
    <profile-fresh-login [logins]="logins" />
    <profile-external-login provider="telegram" [logins]="logins" [enabled]="true" />
  `
})
class LinkHostComponent {
  logins!: AccountLoginsModel;
}

describe('Telegram linking behind the fresh login', () => {
  let fixture: ComponentFixture<LinkHostComponent>;
  let api: AccountApiServiceMock;
  let popup: { closed: boolean; close: Mock };
  let open: Mock;
  let start: Mock;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [LinkHostComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService },
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(AccountApiServiceMock);
    api.account = { ...api.account, freshLoginUntilUtc: null };
    TestBed.inject(FreshLoginService).track(api.account);

    const external = TestBed.inject(ExternalLoginService);
    popup = { closed: false, close: vi.fn() };
    open = vi.spyOn(external, 'openPopup').mockReturnValue(popup as unknown as Window);
    start = vi.spyOn(external, 'start').mockResolvedValue(undefined);

    fixture = TestBed.createComponent(LinkHostComponent);
    fixture.componentInstance.logins = api.account;
    await fixture.whenStable();
  });

  const el = () => fixture.nativeElement as HTMLElement;

  async function settle() {
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  async function askToLink(password: string) {
    el().querySelector<HTMLButtonElement>('#telegram-link')!.click();
    await settle();
    const input = el().querySelector<HTMLInputElement>('#fresh-password')!;
    input.value = password;
    input.dispatchEvent(new Event('input'));
  }

  it('should open the Telegram window in the confirm click and lead it on after the server answered', async () => {
    await askToLink(api.password);
    api.hold();

    el().querySelector<HTMLButtonElement>('#fresh-confirm')!.click();

    // Сам клик: окно уже открыто, а подтверждение ещё не ответило
    expect(open).toHaveBeenCalledTimes(1);
    expect(start).not.toHaveBeenCalled();

    api.release();
    await settle();
    await settle();

    expect(start).toHaveBeenCalledWith('telegram', 'link', '/profile/edit', popup as unknown as Window);
  });

  it('should close the prepared window when the password is wrong', async () => {
    await askToLink('ne-tot');

    el().querySelector<HTMLButtonElement>('#fresh-confirm')!.click();
    await settle();

    expect(open).toHaveBeenCalledTimes(1);
    expect(popup.close).toHaveBeenCalled();
    expect(start).not.toHaveBeenCalled();
    expect(el().querySelector('#fresh-error')).not.toBeNull();
  });

  it('should close the prepared window when the person cancels', async () => {
    await askToLink('ne-tot');
    el().querySelector<HTMLButtonElement>('#fresh-confirm')!.click();
    await settle();
    popup.close.mockClear();
    open.mockClear();

    el().querySelector<HTMLButtonElement>('#fresh-cancel')!.click();
    await settle();

    expect(start).not.toHaveBeenCalled();
    expect(open).not.toHaveBeenCalled();
  });
});
