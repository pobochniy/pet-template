using System.Security.Claims;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Админская схема аутентификации (спец. admin, 3.3): имя схемы и чтение ролей
/// из её токена. Схема продукта остаётся схемой по умолчанию — админская
/// спрашивается только явно, из <c>[AuthorizeAdmin]</c>
/// </summary>
public static class AdminAuth
{
    public const string Scheme = "admin";

    /// <summary>
    /// Тот же токен, но из куки шлюза (3.3). Спрашивает её только
    /// <c>Admin/Auth/Verify</c>; <c>[AuthorizeAdmin]</c> читает лишь заголовок, так что
    /// изменяющий метод кука не открывает
    /// </summary>
    public const string GatewayScheme = "admin-gateway";

    public static AdminRole[] GetAdminRoles(this ClaimsPrincipal principal) =>
        principal.Claims
            .Where(x => x.Type == CustomClaimTypes.AdminRole)
            .Select(x => int.TryParse(x.Value, out var role) ? (AdminRole)role : AdminRole.None)
            .Where(x => x != AdminRole.None)
            .Distinct()
            .Order()
            .ToArray();

    /// <summary>
    /// Пустой <paramref name="required"/> — «любая роль»: экраны на чтение открыты всякому,
    /// кто вообще вошёл в админку (3.2). Иначе — хотя бы одна из перечисленных.
    /// Токен без единой роли не пускает никуда: пустой набор — это не админ (3.1)
    /// </summary>
    public static bool HasAdminRole(this ClaimsPrincipal principal, AdminRole[] required)
    {
        var granted = principal.GetAdminRoles();
        if (granted.Length == 0)
        {
            return false;
        }

        return required.Length == 0 || Array.Exists(required, granted.Contains);
    }
}
