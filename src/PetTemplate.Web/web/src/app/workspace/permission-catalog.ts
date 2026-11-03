import { PermissionEnum } from '../shared/enums/permission.enum';
import { ScopedKey } from '../shared/i18n/generated/i18n-keys';

export interface PermissionInfo {
  readonly permission: PermissionEnum;
  /** Ключ подписи в интерфейсе */
  readonly labelKey: ScopedKey<'workspace'>;
  /** Ключ группы, по которым галки разложены на экране прав */
  readonly groupKey: ScopedKey<'workspace'>;
}

/**
 * Единый список прав с подписями: им пользуются и правка прав участника,
 * и форма выдачи приглашения. Держать два списка нельзя — разъедутся подписи,
 * а вслед за ними и представление о том, что вообще можно выдать.
 *
 * Порядок осмысленный, не алфавитный: сначала управление, потом разделы.
 * Подписи — **ключи**, а не готовые строки: константа читается при загрузке модуля,
 * и текст застрял бы на языке, который стоял в тот момент. Словарь `workspace` — оба
 * экрана, которые рисуют каталог (права участника и выдача приглашения), живут там:
 * в `core` подписи лежали бы в initial-бандле ради ленивого раздела.
 *
 * Точка расширения: раздел проекта, заводящий свои права, дописывает их сюда
 * со своей группой (спец. authorization, 1.1)
 */
export const PERMISSION_CATALOG: readonly PermissionInfo[] = [
  {
    permission: PermissionEnum.PermissionManagement,
    labelKey: 'workspace.permission.permissionManagement',
    groupKey: 'workspace.permissionGroup.permissionManagement'
  },
  {
    permission: PermissionEnum.WorkspaceInfoCrud,
    labelKey: 'workspace.permission.workspaceInfoCrud',
    groupKey: 'workspace.permissionGroup.workspace'
  },
  {
    permission: PermissionEnum.WorkspaceMemberCrud,
    labelKey: 'workspace.permission.workspaceMemberCrud',
    groupKey: 'workspace.permissionGroup.workspace'
  },
  {
    permission: PermissionEnum.WorkspaceMemberRead,
    labelKey: 'workspace.permission.workspaceMemberRead',
    groupKey: 'workspace.permissionGroup.workspace'
  }
];

export interface PermissionGroup {
  readonly key: ScopedKey<'workspace'>;
  readonly permissions: readonly PermissionInfo[];
}

/** Права, разложенные по группам: порядок групп — порядок каталога, не алфавит */
export function groupPermissions(permissions: readonly PermissionInfo[]): PermissionGroup[] {
  return [...new Set(permissions.map(x => x.groupKey))]
    .map(key => ({ key, permissions: permissions.filter(x => x.groupKey === key) }));
}
