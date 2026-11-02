using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Вход на админский эндпоинт (спец. admin, 3.5): токен админской схемы плюс роль.
/// Без ролей в атрибуте — «любая роль», для экранов на чтение (3.2); с ролями —
/// хотя бы одна из них.
///
/// Всякий отказ — <c>404</c>, а не <c>401</c>/<c>403</c>: нет токена, токен продукта,
/// просроченный, отозванный, без нужной роли. Существование админки — не то, о чём надо
/// сообщать, и ответ совпадает с ответом на несуществующий путь (пустым его делает
/// <see cref="AdminNotFound"/>).
///
/// Схема спрашивается явно: по умолчанию аутентифицирует схема продукта, и её
/// <c>User</c> на админском запросе пуст. Здесь он подменяется админским —
/// контроллеру дальше доступны <c>GetUserId()</c> и роли.
///
/// Роли в токене к этой минуте уже урезаны до тех, что лежат в базе (проверка токена
/// схемы <c>admin</c>, <c>ServicesExtensions</c>, 3.3): снятая роль не пускает сразу.
///
/// Вне атрибута живут вход, страница приглашения с его принятием (3.4, 3.5) и подзапрос
/// шлюза <c>Admin/Auth/Verify</c> (3.3), список — <c>AdminWiringTests</c>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AuthorizeAdmin(params AdminRole[] roles) : Attribute, IAsyncAuthorizationFilter
{
    public AdminRole[] Roles { get; } = roles;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var result = await context.HttpContext.AuthenticateAsync(AdminAuth.Scheme);

        if (!result.Succeeded || !result.Principal.HasAdminRole(Roles))
        {
            context.Result = new NotFoundResult();
            return;
        }

        context.HttpContext.User = result.Principal;
    }
}
