import { Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { adminGuard, roleGuard } from './admin.guard';
import { SECTIONS } from '../shell/sections';
import { AdminSessionService } from './admin-session.service';
import { AdminRole } from './admin-role';
import { LoginComponent } from '../login/login.component';

@Component({ template: 'shell' })
class ShellStubComponent {}

/** Ответ HTTP разворачивается цепочкой промисов guard'а и навигации — даём ей дойти */
async function settle(harness: RouterTestingHarness): Promise<void> {
  await harness.fixture.whenStable();
  await new Promise(resolve => setTimeout(resolve));
  harness.detectChanges();
}

/** Роутер запускает guard не сразу: с Angular 21 — позже первого микротика */
function guardStarted(): Promise<void> {
  return new Promise(resolve => setTimeout(resolve));
}

/**
 * После перезагрузки (спец. admin, 3.3) токен в хранилище есть, а кто вошёл — нет:
 * guard спрашивает `Admin/Me`. Кончает сессию только отказ по токену, а не сбой сервера
 */
describe('adminGuard', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('adminToken', 'admin-token');
    localStorage.setItem('adminTokenExpires', new Date(Date.now() + 60 * 60 * 1000).toISOString());

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'login', component: LoginComponent },
          { path: '', component: ShellStubComponent, canActivate: [adminGuard] }
        ])
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.inject(AdminSessionService).end();
    localStorage.clear();
  });

  async function open(respond: (request: ReturnType<HttpTestingController['expectOne']>) => void) {
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/');
    await guardStarted();
    respond(httpMock.expectOne('/api/Admin/Me'));
    await navigation.catch(() => undefined);
    await settle(harness);
    return harness;
  }

  it('should resume the session when Admin/Me answers', async () => {
    const harness = await open(request => request.flush({ userId: 'u-1', userName: 'Oleg', roles: [AdminRole.AdminManagement] }));

    expect(TestBed.inject(Router).url).toBe('/');
    expect(harness.routeNativeElement!.textContent).toContain('shell');
    expect(TestBed.inject(AdminSessionService).admin()?.userName).toBe('Oleg');
  });

  it('should end the session when Admin/Me refuses the token with 404', async () => {
    await open(request => request.flush(null, { status: 404, statusText: 'Not Found' }));

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(localStorage.getItem('adminToken')).toBeNull();
  });

  it('should end the session when Admin/Me refuses the token with 401', async () => {
    await open(request => request.flush(null, { status: 401, statusText: 'Unauthorized' }));

    expect(localStorage.getItem('adminToken')).toBeNull();
  });

  it('should keep the session and offer a retry when the server fails', async () => {
    const harness = await open(request => request.flush(null, { status: 502, statusText: 'Bad Gateway' }));

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(localStorage.getItem('adminToken')).toBe('admin-token');
    expect(harness.routeNativeElement!.querySelector('.unavailable')!.textContent).toContain('502 Bad Gateway');

    (harness.routeNativeElement!.querySelector('.retry') as HTMLButtonElement).click();
    await guardStarted();
    httpMock.expectOne('/api/Admin/Me').flush({ userId: 'u-1', userName: 'Oleg', roles: [AdminRole.AdminManagement] });
    await settle(harness);

    expect(TestBed.inject(Router).url).toBe('/');
  });

  it('should keep the session when the network is down', async () => {
    await open(request => request.error(new ProgressEvent('error')));

    expect(localStorage.getItem('adminToken')).toBe('admin-token');
  });
});

@Component({ template: 'section' })
class SectionStubComponent {}

/** Раздел без роли не открывается и по прямой ссылке — уводит в начало (спец. admin, 4) */
describe('roleGuard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([
          { path: '', component: ShellStubComponent, pathMatch: 'full' },
          ...SECTIONS.map(section => ({
            path: section.path,
            component: SectionStubComponent,
            canActivate: [roleGuard],
            data: { section }
          }))
        ])
      ]
    });
  });

  afterEach(() => TestBed.inject(AdminSessionService).admin.set(null));

  async function open(url: string, roles: AdminRole[]): Promise<string> {
    TestBed.inject(AdminSessionService).admin.set({ userId: 'u-1', userName: 'Oleg', roles });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    return TestBed.inject(Router).url;
  }

  it('should open a section to its role', async () => {
    expect(await open('/admins', [AdminRole.AdminManagement])).toBe('/admins');
  });

  it('should send to the start a direct link to a section without the role', async () => {
    expect(await open('/admins', [AdminRole.Accounts])).toBe('/');
  });

  it('should open accounts to the Accounts role', async () => {
    expect(await open('/accounts', [AdminRole.Accounts])).toBe('/accounts');
  });

  it('should not open accounts to AdminManagement alone', async () => {
    expect(await open('/accounts', [AdminRole.AdminManagement])).toBe('/');
  });

  it('should open the summary to any admin', async () => {
    expect(await open('/summary', [AdminRole.AdminManagement])).toBe('/summary');
  });
});
