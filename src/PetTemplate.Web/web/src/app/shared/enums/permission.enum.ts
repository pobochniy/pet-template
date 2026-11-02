/**
 * Права внутри пространства — то же перечисление, что `PetTemplate.Shared.Enums.PermissionEnum`
 * на сервере: номера обязаны совпадать (спец. authorization, 1.1). Новые права дописываются
 * в конец — числа хранятся в базе и едут в токене.
 *
 * Права разделов проекта — точка расширения: раздел дописывает их сюда, на сервер
 * и в `PERMISSION_CATALOG` (`shared/permissions/permission-catalog.ts`) с подписью
 */
export enum PermissionEnum {
  None = 0,
  PermissionManagement = 1,
  WorkspaceInfoCrud = 2,
  WorkspaceMemberCrud = 3,
  WorkspaceMemberRead = 4
}
