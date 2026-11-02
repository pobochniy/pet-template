import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { PermissionsService } from './shared/services/permissions.service';
import { PermissionEnum } from './shared/enums/permission.enum';

/**
 * Пускает, если есть хотя бы одно из прав `data.allowedRoles`.
 * Маршрут без списка прав закрыт: забытый `data` не должен открывать раздел всем
 */
export const rolesGuard: CanActivateFn = route => {
  const allowed = (route.data['allowedRoles'] ?? []) as PermissionEnum[];
  const permissions = inject(PermissionsService);

  return allowed.some(role => permissions.hasPermission(role));
};
