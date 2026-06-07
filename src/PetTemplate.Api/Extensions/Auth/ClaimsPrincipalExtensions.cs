using System.Security.Claims;

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
}