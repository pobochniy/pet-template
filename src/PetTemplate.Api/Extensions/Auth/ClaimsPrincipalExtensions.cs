using System.Security.Claims;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Extensions.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Получение идентификатора аутентифицированного пользователя
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (value == null) throw new ArgumentException("Не найден");
        
        var id = Guid.Parse(value);
        return id;
    }

    /// <summary>
    /// Права пользователя в конкретном пространстве — клеймы
    /// <see cref="CustomClaimTypes.WorkspacePermission"/>, начинающиеся с <c>"{workspaceId}|"</c>
    /// </summary>
    public static PermissionEnum[] GetWorkspacePermissions(this ClaimsPrincipal principal, Guid workspaceId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.Claims
            .Where(x => x.Type == CustomClaimTypes.WorkspacePermission)
            .Select(x => x.Value.Split('|'))
            .Where(parts => parts.Length == 2 && parts[0] == workspaceId.ToString())
            .Select(parts => (PermissionEnum)int.Parse(parts[1]))
            .ToArray();
    }

    /// <summary>
    /// Есть ли у пользователя хотя бы одно из разрешений в пространстве.
    /// Нужно там, где требуемое разрешение зависит от данных и атрибутом не обойтись
    /// </summary>
    public static bool HasAnyWorkspacePermission(
        this ClaimsPrincipal principal,
        Guid workspaceId,
        params PermissionEnum[] permissions)
    {
        if (permissions.Length == 0) return false;

        var granted = principal.GetWorkspacePermissions(workspaceId);
        return permissions.Any(granted.Contains);
    }

    /// <summary>
    /// Участник ли пользователь пространства — есть ли у него в нём хоть одно право.
    /// Членство и есть права (спец. authorization, 1)
    /// </summary>
    public static bool IsWorkspaceMember(this ClaimsPrincipal principal, Guid workspaceId) =>
        principal.GetWorkspacePermissions(workspaceId).Length > 0;
}
