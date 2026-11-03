import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { ConfirmEmailComponent } from './confirm-email.component';
import { RecoveryApiService } from '../../shared/api/recovery-api.service';
import { RecoveryApiServiceMock } from '../../shared/api/recovery-api.service.mock';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('ConfirmEmailComponent', () => {
  async function render(token: string): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [ConfirmEmailComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: RecoveryApiService, useClass: RecoveryApiServiceMock }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    const fixture = TestBed.createComponent(ConfirmEmailComponent);
    fixture.componentRef.setInput('token', token);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement;
  }

  it('should confirm by a live link', async () => {
    const el = await render('good-token');

    expect(el.querySelector('#confirm-done')?.textContent).toContain('Почта подтверждена');
  });

  it('should explain a stale link', async () => {
    const el = await render('stale-token');

    expect(el.querySelector('#confirm-failed')?.textContent).toContain('Ссылка устарела');
  });
});
