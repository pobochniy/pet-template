import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AccountJournalComponent } from './account-journal.component';
import { AdminAccountJournalEntry } from '../core/admin.model';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('AccountJournalComponent', () => {
  let httpMock: HttpTestingController;

  const deleted: AdminAccountJournalEntry = {
    id: 'j-2',
    createdUtc: '2026-09-29T12:00:00Z',
    kind: 2,
    targetUserId: 'u-lonely',
    targetUserName: 'Lonely',
    targetExists: false,
    adminUserId: 'u-admin',
    adminName: 'Boris',
    reason: 'Сам попросил',
    keptUserId: null,
    keptUserName: null,
    platform: null
  };

  const reset: AdminAccountJournalEntry = {
    ...deleted,
    id: 'j-1',
    kind: 1,
    targetUserId: 'u-masha',
    targetUserName: 'Masha',
    targetExists: true,
    adminName: null,
    adminUserId: null,
    reason: 'Забыла пароль'
  };

  const merge: AdminAccountJournalEntry = {
    ...deleted,
    id: 'j-0',
    kind: null,
    targetUserId: 'u-old',
    targetUserName: 'Masha_old',
    adminUserId: null,
    adminName: null,
    reason: null,
    keptUserId: 'u-masha',
    keptUserName: 'Masha',
    platform: 'telegram'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountJournalComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function render(nextBefore: string | null = null) {
    const fixture = TestBed.createComponent(AccountJournalComponent);
    fixture.detectChanges();
    const request = httpMock.expectOne(r => r.url === '/api/Admin/Accounts/Journal');
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ entries: [deleted, reset, merge], nextBefore });
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('should show a deleted account without a link to its card', async () => {
    const { element } = await render();

    const row = element.querySelectorAll('.entry-row')[0];
    expect(row.textContent).toContain('удаление');
    expect(row.textContent).toContain('Lonely');
    expect(row.textContent).toContain('u-lonely');
    expect(row.textContent).toContain('Boris');
    expect(row.textContent).toContain('«Сам попросил»');
    expect(row.querySelector('a')).toBeNull();
  });

  it('should link a living account to its card', async () => {
    const { element } = await render();

    const row = element.querySelectorAll('.entry-row')[1];
    const link = row.querySelector('a.target-link') as HTMLAnchorElement;
    expect(link.textContent).toContain('Masha');
    expect(link.getAttribute('href')).toBe('/accounts?id=u-masha');
    expect(row.textContent).toContain('ссылка сброса');
    expect(row.textContent).toContain('удалённый админ');
  });

  it('should show a merge as removed and kept accounts', async () => {
    const { element } = await render();

    const row = element.querySelectorAll('.entry-row')[2];
    expect(row.textContent).toContain('слияние: Masha_old удалён,');
    expect(row.textContent).toContain('остался');
    expect(row.textContent).toContain('доказано: telegram');
    expect((row.querySelector('a.kept-link') as HTMLAnchorElement).getAttribute('href')).toBe('/accounts?id=u-masha');
  });

  it('should search by login and continue the shown list with the next page', async () => {
    const { fixture, element } = await render();

    const input = element.querySelector('input[name="query"]') as HTMLInputElement;
    input.value = ' masha ';
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
    (element.querySelector('button[type="submit"]') as HTMLButtonElement).click();

    const search = httpMock.expectOne(r => r.url === '/api/Admin/Accounts/Journal');
    expect(search.request.params.get('query')).toBe('masha');
    search.flush({ entries: [reset], nextBefore: 'j-1' });
    await settle(fixture);

    // Поиск поправили, но «Найти» не нажали — продолжаем показанный список
    input.value = 'boris';
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
    Array.from(element.querySelectorAll('button')).find(x => x.textContent!.trim() === 'Показать ещё')!.click();

    const next = httpMock.expectOne(r => r.url === '/api/Admin/Accounts/Journal');
    expect(next.request.params.get('query')).toBe('masha');
    expect(next.request.params.get('before')).toBe('j-1');
    next.flush({ entries: [merge], nextBefore: null });
    await settle(fixture);

    expect(element.querySelectorAll('.entry-row').length).toBe(2);
    expect(Array.from(element.querySelectorAll('button')).some(x => x.textContent!.includes('Показать ещё'))).toBe(false);
  });
});
