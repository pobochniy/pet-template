import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { requireRoles, rolesGuard } from './roles.guard';
import { PermissionsService } from './shared/services/permissions.service';
import { PermissionEnum } from './shared/enums/permission.enum';

describe('rolesGuard', () => {
  function canActivate(allowedRoles: PermissionEnum[] | undefined, permissions: PermissionEnum[]): boolean | string {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideRouter([])] });
    TestBed.inject(PermissionsService).initPermissions(permissions);

    const data = allowedRoles ? requireRoles(allowedRoles).data : {};
    const route = { data } as unknown as ActivatedRouteSnapshot;

    const result = TestBed.runInInjectionContext(() => rolesGuard(route, {} as RouterStateSnapshot));

    return result instanceof UrlTree ? result.toString() : (result as boolean);
  }

  it('should let in with one of the permissions', () => {
    expect(canActivate([PermissionEnum.PermissionManagement], [PermissionEnum.PermissionManagement])).toBe(true);
  });

  // Отказ — на главную, а не `false`: перепроверка после смены пространства идёт
  // на уже открытом экране, и `false` оставил бы человека там (`recheckRouteAccess`)
  it('should send home without the permission', () => {
    expect(canActivate([PermissionEnum.PermissionManagement], [])).toBe('/');
  });

  it('should close a route without a permission list', () => {
    expect(canActivate(undefined, [PermissionEnum.PermissionManagement])).toBe('/');
  });

  it('should ask to re-run the guard on every check of the same address', () => {
    expect(requireRoles([PermissionEnum.WorkspaceMemberRead]).runGuardsAndResolvers).toBe('always');
  });
});
