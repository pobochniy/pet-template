import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { USER_NAME_BANNER_SNOOZE_MS, UserNameBannerComponent, userNameBannerKey } from './user-name-banner.component';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('UserNameBannerComponent', () => {
  const userId = 'user-banner-1';

  let fixture: ComponentFixture<UserNameBannerComponent>;
  let el: HTMLElement;

  async function setup(generated: boolean) {
    TestBed.configureTestingModule({
      imports: [UserNameBannerComponent],
      providers: [provideZonelessChangeDetection()]
    });

    await loadAllScopes(TestBed.inject(I18nService));
    fixture = TestBed.createComponent(UserNameBannerComponent);
    fixture.componentRef.setInput('userId', userId);
    fixture.componentRef.setInput('userName', 'tg123456789');
    fixture.componentRef.setInput('generated', generated);
    el = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
  }

  beforeEach(() => localStorage.removeItem(userNameBannerKey(userId)));
  afterEach(() => localStorage.removeItem(userNameBannerKey(userId)));

  it('should offer to change an automatic user name', async () => {
    await setup(true);

    expect(el.querySelector('#user-name-banner')!.textContent)
      .toContain('Ваш логин — tg123456789. По нему вас находят участники пространства — придумайте понятный');
  });

  it('should stay hidden for a user name the person chose', async () => {
    await setup(false);

    expect(el.querySelector('#user-name-banner')).toBeNull();
  });

  it('should hide for a day after "Not now"', async () => {
    await setup(true);

    el.querySelector<HTMLButtonElement>('#user-name-banner-later')!.click();
    await fixture.whenStable();

    expect(el.querySelector('#user-name-banner')).toBeNull();
    expect(Number(localStorage.getItem(userNameBannerKey(userId)))).toBeGreaterThan(0);
  });

  it('should show again when the day has passed', async () => {
    localStorage.setItem(userNameBannerKey(userId), String(Date.now() - USER_NAME_BANNER_SNOOZE_MS - 1000));

    await setup(true);

    expect(el.querySelector('#user-name-banner')).not.toBeNull();
  });

  it('should ask the parent to focus the user name field', async () => {
    await setup(true);
    let requested = false;
    fixture.componentInstance.changeRequested.subscribe(() => (requested = true));

    el.querySelector<HTMLButtonElement>('#user-name-banner-change')!.click();

    expect(requested).toBe(true);
  });
});
