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
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date('2026-09-29T12:00:00Z'));

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([])]
    });

    session = TestBed.inject(AdminSessionService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    localStorage.clear();
  });

  it('should hand out the token while it is alive', () => {
    session.start(result(60 * 60 * 1000));

    expect(session.token).toBe('admin-token');
  });

  it('should not hand out an expired token', () => {
    session.start(result(1000));
    jasmine.clock().mockDate(new Date('2026-09-29T12:00:02Z'));

    expect(session.token).toBeNull();
  });

  it('should send to the login page when the hour is over', () => {
    session.start(result(60 * 60 * 1000));

    jasmine.clock().tick(60 * 60 * 1000);

    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(localStorage.getItem('adminToken')).toBeNull();
    expect(session.admin()).toBeNull();
  });

  it('should keep the session until the hour is over', () => {
    session.start(result(60 * 60 * 1000));

    jasmine.clock().tick(59 * 60 * 1000);

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('should treat any role as enough for an open section', () => {
    session.start(result(60 * 60 * 1000, [AdminRole.AdminManagement]));

    expect(session.hasRole(null)).toBeTrue();
    expect(session.hasRole(AdminRole.AdminManagement)).toBeTrue();
  });

  it('should open nothing to a session without roles', () => {
    session.start(result(60 * 60 * 1000, []));

    expect(session.hasRole(null)).toBeFalse();
    expect(session.hasRole(AdminRole.AdminManagement)).toBeFalse();
  });

  it('should end a resumed session at the stored expiry', () => {
    localStorage.setItem('adminToken', 'admin-token');
    localStorage.setItem('adminTokenExpires', new Date(Date.now() + 10 * 60 * 1000).toISOString());

    session.resume({ userId: 'u-1', userName: 'Oleg', roles: [AdminRole.AdminManagement] });
    jasmine.clock().tick(9 * 60 * 1000);
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    jasmine.clock().tick(60 * 1000);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(localStorage.getItem('adminToken')).toBeNull();
  });
});
