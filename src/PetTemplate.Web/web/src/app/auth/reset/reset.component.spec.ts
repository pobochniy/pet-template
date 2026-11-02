import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { ResetComponent } from './reset.component';
import { RecoveryApiService } from '../../shared/api/recovery-api.service';
import { RecoveryApiServiceMock } from '../../shared/api/recovery-api.service.mock';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { UserService } from '../../shared/core/user.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('ResetComponent', () => {
  let fixture: ComponentFixture<ResetComponent>;
  let el: HTMLElement;
  let router: Router;
  let userService: UserService;
  let recovery: RecoveryApiServiceMock;

  async function render(token: string): Promise<void> {
    TestBed.configureTestingModule({
      imports: [ResetComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: RecoveryApiService, useClass: RecoveryApiServiceMock },
        { provide: RecoveryApiServiceMock, useExisting: RecoveryApiService },
        { provide: AccountApiService, useClass: AccountApiServiceMock }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    router = TestBed.inject(Router);
    userService = TestBed.inject(UserService);
    recovery = TestBed.inject(RecoveryApiServiceMock);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    fixture = TestBed.createComponent(ResetComponent);
    fixture.componentRef.setInput('token', token);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }

  async function submit(password: string, confirm = password): Promise<void> {
    for (const [id, value] of [['reset-password', password], ['reset-password-confirm', confirm]]) {
      const input = el.querySelector(`#${id}`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    (el.querySelector('#reset-submit') as HTMLButtonElement).click();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('should set the password and sign in', async () => {
    await render('good-token');

    await submit('Novyi-parol-42');

    expect(userService.isAuthenticated).toBeTrue();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  /** Сброс — тоже вход: интерфейс переключается на язык профиля (спец. accounts, 8.4) */
  it('should switch to the profile language after the reset', async () => {
    await render('good-token');
    recovery.language = 'en';

    await submit('Novyi-parol-42');

    expect(TestBed.inject(I18nService).lang()).toBe('en');
  });

  it('should explain a stale link and offer a new one', async () => {
    await render('stale-token');

    await submit('Novyi-parol-42');

    expect(el.querySelector('#reset-error')?.textContent).toContain('Ссылка устарела');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('should not send mismatched passwords', async () => {
    await render('good-token');

    await submit('Novyi-parol-42', 'Novyi-parol-43');

    expect(el.querySelector('#reset-mismatch')).not.toBeNull();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('should not send a short password', async () => {
    await render('good-token');

    await submit('korotk');

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
