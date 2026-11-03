import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { RecoverComponent } from './recover.component';
import { RecoveryApiService } from '../../shared/api/recovery-api.service';
import { RecoveryApiServiceMock } from '../../shared/api/recovery-api.service.mock';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';

describe('RecoverComponent', () => {
  let fixture: ComponentFixture<RecoverComponent>;
  let el: HTMLElement;
  let recovery: RecoveryApiServiceMock;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [RecoverComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        provideRouter([]),
        { provide: RecoveryApiService, useClass: RecoveryApiServiceMock },
        { provide: RecoveryApiServiceMock, useExisting: RecoveryApiService }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    recovery = TestBed.inject(RecoveryApiServiceMock);
    fixture = TestBed.createComponent(RecoverComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  async function send(value: string): Promise<void> {
    const input = el.querySelector('#recover-email') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));

    (el.querySelector('#recover-send') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('should list mail and support paths in order', () => {
    const ids = Array.from(el.querySelectorAll('.card')).map(x => x.id);

    expect(ids).toEqual(['recover-mail', 'recover-support']);
  });

  it('should answer the same for any email', async () => {
    await send(' nobody@example.com ');

    expect(recovery.resetRequests).toEqual(['nobody@example.com']);
    expect(el.querySelector('#recover-sent')?.textContent).toContain('Если такой аккаунт есть');
  });

  /** Письмо уходит на подтверждённую почту — логин или телефон здесь не принимаются */
  it('should ask for an email, not a login', async () => {
    await send('Oleg');

    expect(recovery.resetRequests).toEqual([]);
    expect(el.querySelector('#recover-email')?.classList).toContain('is-invalid');
    expect(el.querySelector('#recover-sent')).toBeNull();
  });

  /** Контакт поддержки выбирает проект; в шаблоне он пуст — без ссылки */
  it('should name support without a link when no contact is set', () => {
    expect(el.querySelector('#recover-support')?.textContent).toContain('напишите в поддержку');
    expect(el.querySelector('#recover-support-link')).toBeNull();
  });

  /** Путь 1 (8.3): кнопки площадок входят только в существующий аккаунт */
  it('should offer the linked Telegram and VK first', async () => {
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('#recover-external #login-telegram')).not.toBeNull();
    expect(el.querySelector('#recover-external')!.compareDocumentPosition(el.querySelector('#recover-mail')!))
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });
});
