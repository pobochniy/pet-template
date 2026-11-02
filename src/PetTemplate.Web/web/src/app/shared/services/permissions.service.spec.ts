import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { PermissionsService } from './permissions.service';
import { PermissionEnum } from '../enums/permission.enum';

describe('PermissionsService', () => {
  let service: PermissionsService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()]
    });
    service = TestBed.inject(PermissionsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should initialize with empty permissions', () => {
    expect(service.permissions()).toEqual([]);
  });

  it('should set permissions via initPermissions', () => {
    const permissions = [PermissionEnum.PermissionManagement];
    service.initPermissions(permissions);
    expect(service.permissions()).toEqual(permissions);
  });

  it('should return true when user has permission', () => {
    service.initPermissions([PermissionEnum.PermissionManagement]);
    expect(service.hasPermission(PermissionEnum.PermissionManagement)).toBe(true);
  });

  it('should return false when user does not have permission', () => {
    service.initPermissions([PermissionEnum.PermissionManagement]);
    expect(service.hasPermission(PermissionEnum.None)).toBe(false);
  });

  it('should clear permissions', () => {
    service.initPermissions([PermissionEnum.PermissionManagement]);
    service.clearPermissions();
    expect(service.permissions()).toEqual([]);
  });

  it('should handle multiple permissions', () => {
    const permissions = [PermissionEnum.None, PermissionEnum.PermissionManagement];
    service.initPermissions(permissions);
    expect(service.hasPermission(PermissionEnum.None)).toBe(true);
    expect(service.hasPermission(PermissionEnum.PermissionManagement)).toBe(true);
  });
});
