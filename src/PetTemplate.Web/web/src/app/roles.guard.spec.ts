import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { rolesGuard } from './roles.guard';
import { PermissionsService } from './shared/services/permissions.service';
import { PermissionEnum } from './shared/enums/permission.enum';

describe('rolesGuard', () => {
  function canActivate(allowedRoles: PermissionEnum[] | undefined, permissions: PermissionEnum[]): boolean {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    TestBed.inject(PermissionsService).initPermissions(permissions);

    const route = { data: allowedRoles ? { allowedRoles } : {} } as unknown as ActivatedRouteSnapshot;

    return TestBed.runInInjectionContext(() => rolesGuard(route, {} as RouterStateSnapshot)) as boolean;
  }

  it('should let in with one of the permissions', () => {
    expect(canActivate([PermissionEnum.PermissionManagement], [PermissionEnum.PermissionManagement])).toBeTrue();
  });

  it('should not let in without the permission', () => {
    expect(canActivate([PermissionEnum.PermissionManagement], [])).toBeFalse();
  });

  it('should close a route without a permission list', () => {
    expect(canActivate(undefined, [PermissionEnum.PermissionManagement])).toBeFalse();
  });
});
