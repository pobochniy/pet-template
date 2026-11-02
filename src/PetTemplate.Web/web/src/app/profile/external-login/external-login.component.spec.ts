import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { ProfileExternalLoginComponent } from './external-login.component';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { FreshLoginService } from '../fresh-login/fresh-login.service';
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
    const start = spyOn(TestBed.inject(ExternalLoginService), 'start').and.resolveTo();

    await click('telegram-link');

    expect(start).toHaveBeenCalledWith('telegram', 'link', '/profile/edit');
  });

  /** Площадка не настроена на сервере — привязать некуда */
  it('should not offer linking when the platform is off', async () => {
    await render({ telegramLinked: false }, false);

    expect(el.querySelector('#telegram-link')).toBeNull();
  });

  it('should unlink when another way to sign in remains', async () => {
    await render({ telegramLinked: true, hasPassword: true, loginCount: 2 });

    await click('telegram-unlink');

    expect(changed[0].telegramLinked).toBeFalse();
  });

  /** Последний способ войти не отвязывается (раздел 3): кнопка выключена, подсказка видна */
  it('should not unlink the last way to sign in', async () => {
    await render({ telegramLinked: true, hasPassword: false, loginCount: 1 });

    expect(el.querySelector<HTMLButtonElement>('#telegram-unlink')!.disabled).toBeTrue();
    expect(el.querySelector('#telegram-last-hint')?.textContent).toContain('Сначала задайте пароль');
  });
});
