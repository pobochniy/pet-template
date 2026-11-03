import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SummaryComponent } from './summary.component';
import { AdminSessionService } from '../core/admin-session.service';
import { AdminRole } from '../core/admin-role';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

/** «Сводка» (спец. admin, 4 и 6): версия API, время сервера, кто вошёл; реиндекс поиска (спец. search, 5) */
describe('SummaryComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    // `end()` в `afterEach` уводит на `/login`: без маршрута переход падает уже после теста
    TestBed.configureTestingModule({
      imports: [SummaryComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([{ path: 'login', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);

    TestBed.inject(AdminSessionService).start({
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      admin: { userId: 'u-1', userName: 'Oleg', roles: [AdminRole.AdminManagement] }
    });
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.inject(AdminSessionService).end();
    localStorage.clear();
  });

  async function render() {
    const fixture = TestBed.createComponent(SummaryComponent);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function cell(element: HTMLElement, id: string): string {
    return element.querySelector(`#${id}`)!.textContent!.trim();
  }

  it('should show the API version and the server time', async () => {
    const { fixture, element } = await render();

    httpMock.expectOne('/api/Admin/Summary').flush({ version: '1.0.0+abc', serverTimeUtc: '2026-10-25T09:30:00Z' });
    await settle(fixture);

    expect(cell(element, 'api-version')).toBe('1.0.0+abc');
    expect(cell(element, 'server-time')).toMatch(/^25\.10\.2026 \d\d:30:00$/);
  });

  it('should show who is in, the roles and the session end', async () => {
    const { fixture, element } = await render();

    httpMock.expectOne('/api/Admin/Summary').flush({ version: '1', serverTimeUtc: '2026-10-25T09:30:00Z' });
    await settle(fixture);

    expect(cell(element, 'admin-name')).toBe('Oleg');
    expect(cell(element, 'admin-roles')).toBe('Управление админами');
    expect(cell(element, 'session-until')).toMatch(/^\d\d\.\d\d\.\d{4} \d\d:\d\d$/);
  });

  it('should show the server text when the summary fails', async () => {
    const { fixture, element } = await render();

    httpMock.expectOne('/api/Admin/Summary').flush(null, { status: 502, statusText: 'Bad Gateway' });
    await settle(fixture);

    expect(element.querySelector('.alert-danger')!.textContent).toContain('502 Bad Gateway');
    expect(cell(element, 'api-version')).toBe('—');
  });

  it('should queue the search reindex for the admin manager', async () => {
    const { fixture, element } = await render();
    httpMock.expectOne('/api/Admin/Summary').flush({ version: '1', serverTimeUtc: '2026-10-25T09:30:00Z' });
    await settle(fixture);

    (element.querySelector('#reindex-search') as HTMLButtonElement).click();
    const request = httpMock.expectOne('/api/Admin/Search/Reindex');
    expect(request.request.method).toBe('POST');
    request.flush(null);
    await settle(fixture);

    expect(element.querySelector('#reindex-queued')).not.toBeNull();
  });

  it('should hide the reindex without the admin manager role', async () => {
    TestBed.inject(AdminSessionService).start({
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      admin: { userId: 'u-1', userName: 'Oleg', roles: [AdminRole.Accounts] }
    });
    const { fixture, element } = await render();
    httpMock.expectOne('/api/Admin/Summary').flush({ version: '1', serverTimeUtc: '2026-10-25T09:30:00Z' });
    await settle(fixture);

    expect(element.querySelector('#reindex-search')).toBeNull();
  });
});
