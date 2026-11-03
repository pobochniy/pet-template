import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { DeleteAccountComponent } from './delete-account.component';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { TokenService } from '../../shared/api/token.service';
import { UserService } from '../../shared/core/user.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { profileDto, sessionDto } from '../../shared/api/dto.mock';

describe('DeleteAccountComponent', () => {
  let fixture: ComponentFixture<DeleteAccountComponent>;
  let el: HTMLElement;
  let api: AccountApiServiceMock;
  let user: UserService;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [DeleteAccountComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService }
      ]
    });

    api = TestBed.inject(AccountApiServiceMock);
    user = TestBed.inject(UserService);
    user.applySession(sessionDto({ token: 'token-1', profile: profileDto({ id: 'user-1', userName: 'tester', avatarId: 0, comment: null, isAdult: true, hasAcceptedTerms: true }), permissionsByWorkspace: {} }));
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    await loadAllScopes(TestBed.inject(I18nService));
    fixture = TestBed.createComponent(DeleteAccountComponent);
    el = fixture.nativeElement;
    await render();
  });

  afterEach(() => {
    user.clearProfile();
  });

  async function render() {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(value: string) {
    const input = el.querySelector<HTMLInputElement>('#delete-confirm')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  const button = () => el.querySelector<HTMLButtonElement>('#delete-account')!;

  /** Экран говорит, какое пространство удалится с содержимым, а какое останется (раздел 9) */
  it('should tell which workspaces go away and which stay', () => {
    expect(el.querySelector('#delete-solo')?.textContent).toContain('Моё пространство');
    expect(el.querySelector('#delete-solo')?.textContent).toContain('со всем содержимым');
    expect(el.querySelector('#delete-shared')?.textContent).toContain('Рога и Копыта');
    expect(el.querySelector('#delete-shared')?.textContent).toContain('boris');
  });

  it('should keep the button disabled until the own username is typed', () => {
    type('boris');
    expect(button().disabled).toBe(true);

    type('TESTER');
    expect(button().disabled).toBe(false);
  });

  /** Удалённый аккаунт сервер уже не примет — сессия убирается без обращения к нему */
  it('should delete the account and drop the session', async () => {
    type('tester');

    button().click();
    await render();

    expect(api.deletedWith).toBe('tester');
    expect(user.isAuthenticated).toBe(false);
    expect(TestBed.inject(TokenService).Token).toBe('');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/auth/login');
  });

  /** Гость — на вход, без запроса карточки: удалять ему нечего */
  it('should send a guest to the login screen', async () => {
    user.clearProfile();
    const preview = vi.spyOn(api, 'deletePreview');
    fixture = TestBed.createComponent(DeleteAccountComponent);
    await render();

    expect(preview).not.toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/auth/login');
  });
});
