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

        // Без клейма сюда доходит только эндпоинт, забывший [Authorize], — ошибка разметки, 500
        var value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? throw new InvalidOperationException("No NameIdentifier claim: the endpoint is not authorized");

        var id = Guid.Parse(value);
        return id;
    }
}
