import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { ExternalButtonsComponent } from './external-buttons.component';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('ExternalButtonsComponent', () => {
  let fixture: ComponentFixture<ExternalButtonsComponent>;
  let el: HTMLElement;

  async function render(arrange: (api: ExternalLoginApiServiceMock) => void = () => {}, purpose: 'login' | 'recover' = 'login') {
    TestBed.configureTestingModule({
      imports: [ExternalButtonsComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        { provide: ExternalLoginApiServiceMock, useExisting: ExternalLoginApiService }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    arrange(TestBed.inject(ExternalLoginApiServiceMock));
    fixture = TestBed.createComponent(ExternalButtonsComponent);
    fixture.componentRef.setInput('purpose', purpose);
    fixture.componentRef.setInput('returnUrl', '/invite/abc');
    el = fixture.nativeElement;
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('should show both platforms the server configured', async () => {
    await render();

    expect(el.querySelector('#login-telegram')).not.toBeNull();
    expect(el.querySelector('#login-vk')).not.toBeNull();
  });

  /** Пустой id — кнопки нет (5.1) */
  it('should hide a platform without an id', async () => {
    await render(api => api.config.telegramBotId = '');

    expect(el.querySelector('#login-telegram')).toBeNull();
    expect(el.querySelector('#login-vk')).not.toBeNull();
  });

  it('should render nothing when no platform is configured', async () => {
    await render(api => api.config = { ...api.config, telegramBotId: '', vkAppId: '' });

    expect(el.querySelector('#external-logins')).toBeNull();
  });

  it('should start the platform with the purpose and the return address', async () => {
    await render(() => {}, 'recover');
    const start = spyOn(TestBed.inject(ExternalLoginService), 'start').and.resolveTo();

    el.querySelector<HTMLButtonElement>('#login-vk')!.click();
    await fixture.whenStable();

    expect(start).toHaveBeenCalledWith('vk', 'recover', '/invite/abc');
  });

  /** Окно Telegram заблокировано — кнопки снова живые, под ними отказ */
  it('should report a blocked sign-in window', async () => {
    await render();
    spyOn(TestBed.inject(ExternalLoginService), 'openPopup').and.returnValue(null);

    el.querySelector<HTMLButtonElement>('#login-telegram')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('#external-start-failed')).not.toBeNull();
    expect(el.querySelector<HTMLButtonElement>('#login-telegram')!.disabled).toBeFalse();
  });
});
