import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ShellComponent } from './shell.component';
import { AdminSessionService } from '../core/admin-session.service';
import { AdminRole } from '../core/admin-role';

describe('ShellComponent', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
  });

  afterEach(() => localStorage.clear());

  async function render(roles: AdminRole[]) {
    TestBed.inject(AdminSessionService).start({
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      admin: { userId: 'u-1', userName: 'Oleg', roles }
    });

    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    return fixture;
  }

  async function tabs(roles: AdminRole[]): Promise<string[]> {
    const fixture = await render(roles);

    const links = (fixture.nativeElement as HTMLElement).querySelectorAll('.nav-link');
    return Array.from(links).map(link => link.textContent!.trim());
  }

  it('should show open sections (summary, models, spend) to any admin and admins with the access log only to AdminManagement', async () => {
    expect(await tabs([AdminRole.AdminManagement])).toEqual(['Сводка', 'Модели', 'Расход', 'Админы', 'Журнал доступа']);
  });

  it('should show account sections only to the Accounts role', async () => {
    expect(await tabs([AdminRole.Accounts])).toEqual(['Сводка', 'Модели', 'Аккаунты', 'Журнал аккаунтов', 'Расход']);
  });

  it('should show dialogs only to the Dialogs role, without the access log', async () => {
    expect(await tabs([AdminRole.Dialogs])).toEqual(['Сводка', 'Модели', 'Диалоги', 'Расход']);
  });

  it('should show who is in and until when the session lasts', async () => {
    const fixture = await render([AdminRole.AdminManagement]);
    const header = (fixture.nativeElement as HTMLElement).textContent!;

    expect(header).toContain('PetTemplate · админка');
    expect(header).toContain('Oleg');
    expect(header).toMatch(/сессия до \d\d:\d\d/);
  });

  describe('«Выйти»', () => {
    async function logOut(answer: 'ok' | 'gone') {
      const fixture = await render([AdminRole.AdminManagement]);
      const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
      const http = TestBed.inject(HttpTestingController);

      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('#logout')!.click();
      const request = http.expectOne('/api/Admin/Logout');
      expect(request.request.method).toBe('POST');
      if (answer === 'ok') request.flush(null);
      else request.flush(null, { status: 404, statusText: 'Not Found' });
      await fixture.whenStable();

      return navigate;
    }

    it('should tell the server and end the session', async () => {
      const navigate = await logOut('ok');

      expect(TestBed.inject(AdminSessionService).isActive()).toBeFalse();
      expect(navigate).toHaveBeenCalledWith('/login');
    });

    it('should end the session even if the server no longer knows the token', async () => {
      const navigate = await logOut('gone');

      expect(localStorage.getItem('adminToken')).toBeNull();
      expect(navigate).toHaveBeenCalledWith('/login');
    });
  });
});
