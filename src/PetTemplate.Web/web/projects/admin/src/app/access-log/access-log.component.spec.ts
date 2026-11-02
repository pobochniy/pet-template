import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AccessLogComponent } from './access-log.component';
import { AdminAccessLogEntry, AdminMember } from '../core/admin.model';
import { AdminRole } from '../core/admin-role';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

/** Журнал доступа к диалогам (спец. admin, 16.2) */
describe('AccessLogComponent', () => {
  let httpMock: HttpTestingController;

  const admins: AdminMember[] = [
    { userId: 'u-2', userName: 'Vasya', roles: [{ role: AdminRole.Dialogs, grantedUtc: '2026-09-01T00:00:00Z', grantedByName: null }] }
  ];

  const entry: AdminAccessLogEntry = {
    id: 'l-1',
    createdUtc: '2026-09-29T12:00:00Z',
    adminUserId: 'u-2',
    adminName: 'Vasya',
    runId: 'run-1',
    workspaceId: 'ws-1',
    workspaceName: 'Рога и Копыта'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccessLogComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()]
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function render(nextBefore: string | null = null) {
    const fixture = TestBed.createComponent(AccessLogComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/Admin/Admins').flush(admins);
    httpMock.expectOne(r => r.url === '/api/Admin/AccessLog').flush({ entries: [entry], nextBefore });
    // Два ответа собираются через Promise.all — лишний круг, пока он развернётся
    await settle(fixture);
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('should show who opened whose run and when', async () => {
    const { element } = await render();

    const row = element.querySelector('.entry-row')!;
    expect(row.textContent).toContain('Vasya');
    expect(row.textContent).toContain('«Рога и Копыта»');
    expect(row.textContent).toContain('run-1');
  });

  it('should name a removed admin', async () => {
    const fixture = TestBed.createComponent(AccessLogComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/Admin/Admins').flush(admins);
    httpMock.expectOne(r => r.url === '/api/Admin/AccessLog').flush({
      entries: [{ ...entry, adminUserId: null, adminName: null }],
      nextBefore: null
    });
    await settle(fixture);
    await settle(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('.entry-row')!.textContent).toContain('удалённый админ');
  });

  it('should filter by admin and period', async () => {
    const { fixture, element } = await render();

    const select = element.querySelector('select[name="admin"]') as HTMLSelectElement;
    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    const from = element.querySelector('input[name="from"]') as HTMLInputElement;
    from.value = '2026-09-01';
    from.dispatchEvent(new Event('input'));
    await settle(fixture);

    (element.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await settle(fixture);

    const request = httpMock.expectOne(r => r.url === '/api/Admin/AccessLog');
    expect(request.request.params.get('adminUserId')).toBe('u-2');
    expect(request.request.params.get('from')).toBe('2026-09-01');
    expect(request.request.params.has('to')).toBeFalse();
    request.flush({ entries: [], nextBefore: null });
    await settle(fixture);
  });

  it('should offer no way to delete an entry', async () => {
    const { element } = await render();

    expect(element.querySelector('.entry-row button')).toBeNull();
  });

  it('should continue the shown list, not the edited filter, with the next page', async () => {
    const { fixture, element } = await render('l-1');

    // Период поправили, но «Найти» не нажали — список на экране всё ещё за всё время
    const from = element.querySelector('input[name="from"]') as HTMLInputElement;
    from.value = '2026-09-01';
    from.dispatchEvent(new Event('input'));
    await settle(fixture);
    Array.from(element.querySelectorAll('button')).find(x => x.textContent!.trim() === 'Показать ещё')!.click();
    const request = httpMock.expectOne(r => r.url === '/api/Admin/AccessLog');

    expect(request.request.params.get('before')).toBe('l-1');
    expect(request.request.params.has('from')).toBeFalse();
    request.flush({ entries: [{ ...entry, id: 'l-0', runId: 'run-0', workspaceName: null }], nextBefore: null });
    await settle(fixture);

    const rows = element.querySelectorAll('.entry-row');
    expect(rows.length).toBe(2);
    expect(rows[1].textContent).toContain('пространство ws-1');
  });
});
