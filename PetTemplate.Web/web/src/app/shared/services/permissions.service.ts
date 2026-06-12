import { Injectable, signal } from '@angular/core';
import { PermissionEnum } from '../enums/permission.enum';

@Injectable({ providedIn: 'root' })
export class PermissionsService {
  private _permissions = signal<PermissionEnum[]>([]);

  get permissions() {
    return this._permissions.asReadonly();
  }

  initPermissions(permissions: PermissionEnum[]): void {
    this._permissions.set(permissions);
  }

  hasPermission(permission: PermissionEnum): boolean {
    return this._permissions().includes(permission);
  }

  clearPermissions(): void {
    this._permissions.set([]);
  }
}
