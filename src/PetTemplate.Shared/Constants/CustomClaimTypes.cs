namespace PetTemplate.Shared.Constants;

public static class CustomClaimTypes
{
    /// <summary>
    /// Право в пространстве: по клейму на право, значение — <c>"{workspaceId}|{(int)permission}"</c>
    /// (спец. authorization, 2.1). Глобальных <c>ClaimTypes.Role</c> в токене нет:
    /// права без пространства не бывает
    /// </summary>
    public const string WorkspacePermission = "ws_permission";

    /// <summary>
    /// Админская роль — числом <c>AdminRole</c>, по клейму на роль (спец. admin, 3.3).
    /// Живёт только в токене админской схемы: в токене продукта его не бывает
    /// </summary>
    public const string AdminRole = "admin_role";
}
