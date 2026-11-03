import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Route, Router } from '@angular/router';
import { PermissionsService } from './shared/services/permissions.service';
import { PermissionEnum } from './shared/enums/permission.enum';

/** Данные маршрута, закрытого правами: любое из них пускает */
export interface RolesRouteData {
  readonly allowedRoles: readonly PermissionEnum[];
}

/**
 * Пускает, если есть хотя бы одно из прав `data.allowedRoles`.
 * Маршрут без списка прав закрыт: забытый `data` не должен открывать раздел всем.
 *
 * Отказ — переход на главную, а не `false`: проверка повторяется и на уже открытом
 * экране (сменили пространство в шапке — `recheckRouteAccess`), и `false` оставил бы
 * человека на странице, куда его больше не пускают
 */
export const rolesGuard: CanActivateFn = route => {
  const permissions = inject(PermissionsService);

  if (allowedRolesOf(route).some(role => permissions.hasPermission(role))) return true;

  return inject(Router).parseUrl('/');
};

/**
 * Маршрут, закрытый правами активного пространства. Типизированная фабрика вместо
 * `data: { allowedRoles }` руками: опечатка в ключе молча закрыла бы раздел всем.
 *
 * `runGuardsAndResolvers: 'always'` — чтобы гард повторялся на перепроверке того же
 * адреса после смены пространства или пользователя (`recheckRouteAccess`)
 */
export function requireRoles(
  allowedRoles: readonly PermissionEnum[]
): Required<Pick<Route, 'canActivate' | 'data' | 'runGuardsAndResolvers'>> {
  const data: RolesRouteData = { allowedRoles };

  return { canActivate: [rolesGuard], data: { ...data }, runGuardsAndResolvers: 'always' };
}

function allowedRolesOf(route: ActivatedRouteSnapshot): readonly PermissionEnum[] {
  return (route.data as Partial<RolesRouteData>).allowedRoles ?? [];
}
