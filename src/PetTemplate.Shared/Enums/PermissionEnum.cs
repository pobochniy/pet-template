namespace PetTemplate.Shared.Enums;

/// <summary>
/// Права участника внутри пространства (спец. authorization, 1.1). Числа хранятся в базе
/// (<c>user_in_permissions.permission_id</c>) и едут клиенту в ответе входа, поэтому новые права
/// дописываются в конец, а существующие не переставляются.
///
/// В ядре только права на само пространство и его состав. Права разделов проекта —
/// точка расширения: раздел дописывает свои значения сюда и в клиентского двойника,
/// получает их в наборе владельца сам (<c>WorkspaceService</c> выдаёт создателю все
/// значения, кроме <see cref="None"/>) и выдаёт существующим участникам миграцией данных.
///
/// должно быть продублировано в
/// web/src/app/shared/enums/permission.enum.ts
/// </summary>
public enum PermissionEnum
{
    None,

    /// <summary>Выдача и снятие прав участникам, удаление участника</summary>
    PermissionManagement,

    /// <summary>Правка самого пространства — название</summary>
    WorkspaceInfoCrud,

    /// <summary>Состав: выдача и отзыв приглашений</summary>
    WorkspaceMemberCrud,

    /// <summary>Состав: страница участников и список выданных ссылок — только чтение</summary>
    WorkspaceMemberRead
}
