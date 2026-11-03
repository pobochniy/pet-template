import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { AdminSessionService } from './admin-session.service';
import { AdminRole } from './admin-role';
import { AdminLoginResult } from './admin.model';

describe('AdminSessionService', () => {
  let session: AdminSessionService;
  let router: Router;

  function result(expiresInMs: number, roles: AdminRole[] = [AdminRole.AdminManagement]): AdminLoginResult {
    return {
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + expiresInMs).toISOString(),
      admin: { userId: 'u-1', userName: 'Oleg', roles }
    };
  }

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-29T12:00:00Z'));

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([])]
    });

    session = TestBed.inject(AdminSessionService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  it('should hand out the token while it is alive', () => {
    session.start(result(60 * 60 * 1000));

    expect(session.token).toBe('admin-token');
  });

  it('should not hand out an expired token', () => {
    session.start(result(1000));
    vi.setSystemTime(new Date('2026-09-29T12:00:02Z'));

    expect(session.token).toBeNull();
  });

  it('should send to the login page when the hour is over', () => {
    session.start(result(60 * 60 * 1000));

    vi.advanceTimersByTime(60 * 60 * 1000);

    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(localStorage.getItem('adminToken')).toBeNull();
    expect(session.admin()).toBeNull();
  });

  it('should keep the session until the hour is over', () => {
    session.start(result(60 * 60 * 1000));

    vi.advanceTimersByTime(59 * 60 * 1000);

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('should treat any role as enough for an open section', () => {
    session.start(result(60 * 60 * 1000, [AdminRole.AdminManagement]));

    expect(session.hasRole(null)).toBe(true);
    expect(session.hasRole(AdminRole.AdminManagement)).toBe(true);
  });

  it('should open nothing to a session without roles', () => {
    session.start(result(60 * 60 * 1000, []));

    expect(session.hasRole(null)).toBe(false);
    expect(session.hasRole(AdminRole.AdminManagement)).toBe(false);
  });

  it('should end a resumed session at the stored expiry', () => {
    localStorage.setItem('adminToken', 'admin-token');
    localStorage.setItem('adminTokenExpires', new Date(Date.now() + 10 * 60 * 1000).toISOString());

    session.resume({ userId: 'u-1', userName: 'Oleg', roles: [AdminRole.AdminManagement] });
    vi.advanceTimersByTime(9 * 60 * 1000);
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    vi.advanceTimersByTime(60 * 1000);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(localStorage.getItem('adminToken')).toBeNull();
  });
});
