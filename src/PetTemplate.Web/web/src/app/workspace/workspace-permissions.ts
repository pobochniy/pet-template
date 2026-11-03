import { PermissionEnum } from '../shared/enums/permission.enum';

/**
 * Права, любое из которых открывает страницу участников (спец. authorization, 7.3).
 * Их три, потому что на странице три раздела, закрытых каждый своим правом:
 * приглашения — `WorkspaceMemberCrud`, правка доступов и «Выгнать» —
 * `PermissionManagement`, сам состав — ещё и `WorkspaceMemberRead`.
 *
 * Список живёт отдельным модулем, а не в компоненте: его читают и маршрут
 * (`workspace.routes.ts`), и меню, и список пространств — он по нему решает,
 * показывать ли строке кнопку «Участники». Импорт из компонента затянул бы ленивый
 * чанк в маршруты, то есть в initial-бандл
 */
export const MEMBERS_PAGE_PERMISSIONS: readonly PermissionEnum[] = [
  PermissionEnum.WorkspaceMemberCrud,
  PermissionEnum.WorkspaceMemberRead,
  PermissionEnum.PermissionManagement
];
