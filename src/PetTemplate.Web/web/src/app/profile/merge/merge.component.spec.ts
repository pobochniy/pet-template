import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { MergeComponent } from './merge.component';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { TokenService } from '../../shared/api/token.service';
import { MergeStateService } from '../../shared/core/merge-state.service';
import { UserService } from '../../shared/core/user.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('MergeComponent', () => {
  let fixture: ComponentFixture<MergeComponent>;
  let el: HTMLElement;
  let api: AccountApiServiceMock;
  let state: MergeStateService;
  let user: UserService;
  let router: Router;

  async function setup(arrange: (mock: AccountApiServiceMock) => void = () => {}, withPreview = true, signedIn = true) {
    TestBed.configureTestingModule({
      imports: [MergeComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService }
      ]
    });

    api = TestBed.inject(AccountApiServiceMock);
    arrange(api);
    user = TestBed.inject(UserService);
    TestBed.inject(TokenService).Token = 'token-0';
    if (signedIn) {
      user.setProfile({ id: 'user-1', userName: 'tester', avatarId: 0, comment: null, isAdult: true, hasAcceptedTerms: true });
    } else {
      user.clearProfile();
    }
    state = TestBed.inject(MergeStateService);
    if (withPreview) state.open(api.preview);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    await loadAllScopes(TestBed.inject(I18nService));
    fixture = TestBed.createComponent(MergeComponent);
    el = fixture.nativeElement;
    await render();
  }

  afterEach(() => {
    user.clearProfile();
    TestBed.inject(TokenService).Token = '';
  });

  async function render() {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function keep(userId: string) {
    el.querySelector<HTMLInputElement>(`#keep-${userId}`)!.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('#merge-next')!.click();
    fixture.detectChanges();
  }

  /** По умолчанию не выбран ни один: выбор необратим (7.3) */
  it('should show both accounts with nothing chosen', async () => {
    await setup();

    expect(el.textContent).toContain('tester');
    expect(el.textContent).toContain('tg123456789');
    expect(el.querySelector('#merge-next')).toBeNull();
  });

  /** Подтверждение — словами: что удалится, что перейдёт, кнопка с логинами (7.3) */
  it('should spell out the consequences before merging', async () => {
    await setup();

    await keep('user-1');

    const text = el.querySelector('#merge-confirmation')!.textContent!;
    expect(text).toContain('«Шарики и Палочки» (только он) удалится');
    expect(text).toContain('участие в «Рога и Копыта» перейдёт к tester');
    expect(text).toContain('Вход через Telegram перейдёт к tester');
    expect(el.querySelector('#merge-confirm')!.textContent).toContain('Удалить tg123456789 и оставить tester');
  });

  it('should merge and take the session of the kept account', async () => {
    await setup();
    await keep('user-1');

    el.querySelector<HTMLButtonElement>('#merge-confirm')!.click();
    await render();

    expect(api.merged).toEqual({ ticket: 'ticket-1', keepUserId: 'user-1' });
    expect(TestBed.inject(TokenService).Token).toBe('token-1');
    expect(state.preview()).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/profile/edit');
  });

  /** Аккаунт с ролью в админке может только остаться — удалить его выбором нельзя */
  it('should not let an admin account be removed', async () => {
    await setup(mock => mock.other = { ...mock.other, canBeRemoved: false });

    expect(el.querySelector<HTMLInputElement>('#keep-user-1')!.disabled).toBeTrue();
    expect(el.querySelector<HTMLInputElement>('#keep-user-2')!.disabled).toBeFalse();
  });

  /** Второй заведён меньше суток назад — остаться может только текущий (7.3) */
  it('should not let a young other account be kept', async () => {
    await setup(mock => mock.canKeepOther = false);

    expect(el.querySelector<HTMLInputElement>('#keep-user-2')!.disabled).toBeTrue();
    expect(el.querySelector<HTMLInputElement>('#keep-user-1')!.disabled).toBeFalse();
    expect(el.querySelector('.young-account')).not.toBeNull();
  });

  /** Состав поменялся с просмотра — отказ, ключ погашен, смотреть заново (7.4) */
  it('should drop the preview after a conflict', async () => {
    await setup(mock => mock.mergeConflict = true);
    await keep('user-1');

    el.querySelector<HTMLButtonElement>('#merge-confirm')!.click();
    await render();

    expect(el.querySelector('#merge-error')?.textContent).toContain('поменялся состав пространств');
    expect(el.querySelector('#merge-empty')).not.toBeNull();
  });

  it('should ask to start over when there is no preview', async () => {
    await setup(() => {}, false);

    expect(el.querySelector('#merge-empty')).not.toBeNull();
  });

  /** Гость — на вход: экран своего аккаунта ему показывать нечего */
  it('should send a guest to the login screen', async () => {
    await setup(() => {}, false, false);

    expect(router.navigateByUrl).toHaveBeenCalledWith('/auth/login');
  });
});
