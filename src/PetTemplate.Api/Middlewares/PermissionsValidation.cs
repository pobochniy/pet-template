using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PetTemplate.Api.Extensions;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Проверка <see cref="AuthorizePermissions"/> (спец. authorization, 1.2) — в два шага.
///
/// Фильтр авторизации — до разбора тела и до <c>[ValidateRequest]</c>: посторонний получает
/// <c>403</c>, а не <c>422</c> с подробностями формы, в которую ему нельзя.
/// <list type="number">
/// <item>метод помечен ещё и <see cref="OutsideWorkspace"/> — ошибка разметки, исключение;</item>
/// <item>не вошёл — <c>401</c>;</item>
/// <item>нет заголовка <c>X-Workspace-Id</c> или в нём не <c>Guid</c> — <c>400</c>;</item>
/// <item>нет ни одного из прав в этом пространстве (без прав в атрибуте — ни одного
/// права вовсе, то есть не участник) — <c>403</c>.</item>
/// </list>
///
/// Фильтр действия — после разбора, но раньше <c>[ValidateRequest]</c>
/// (<see cref="AuthorizePermissions.FilterOrder"/>): пространство из заголовка не совпало
/// с <c>workspaceId</c> в query или маршруте либо со свойством <c>WorkspaceId</c> в теле —
/// <c>403</c>: иначе право, выданное в своём пространстве, открывало бы чужое, названное
/// в параметрах.
///
/// Права — из базы, отметкой <see cref="SessionGuard"/> (кэш на секунды, сброс после
/// каждой правки прав), а не из токена: отозванное право не живёт до истечения сессии
/// (спец. authorization, 2.3)
/// </summary>
public class PermissionsValidation(SessionGuard sessions) : IAsyncAuthorizationFilter, IActionFilter
{
    public PermissionEnum[] Permissions { get; set; } = [];

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Два признака на одном эндпоинте — ошибка разметки, а не запрос: молча
        // выбрать один значило бы либо требовать пространство у админки, либо пускать
        // в пространство без проверки (спец. admin, 3.5)
        if (context.ActionDescriptor.EndpointMetadata.OfType<OutsideWorkspace>().Any())
        {
            throw new InvalidOperationException(
                $"{context.ActionDescriptor.DisplayName}: [AuthorizePermissions] together with [OutsideWorkspace]");
        }

        var user = context.HttpContext.User;

        if (user.Identity == null || !user.Identity.IsAuthenticated)
        {
            context.Result = new StatusCodeResult(401);
            return;
        }

        Guid workspaceId;
        try
        {
            workspaceId = context.HttpContext.GetWorkspaceId();
        }
        catch (BadHttpRequestException e)
        {
            context.Result = new BadRequestObjectResult(e.Message);
            return;
        }

        var granted = await sessions.PermissionsIn(user.GetUserId(), workspaceId, context.HttpContext.RequestAborted);

        var isAllowed = Permissions.Length == 0
            ? granted.Length > 0
            : Permissions.Any(granted.Contains);

        if (!isAllowed) context.Result = new StatusCodeResult(403);
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        // Заголовок уже проверен на шаге авторизации: без него сюда не доходят
        if (!ValidateWorkspaceContext(context, context.HttpContext.GetWorkspaceId()))
        {
            context.Result = new StatusCodeResult(403);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    /// <summary>
    /// Пространство в параметрах совпадает с заголовком. Пустой <c>Guid</c> в теле
    /// не считается расхождением: поле просто не прислали, и сервис возьмёт пространство
    /// из заголовка или откажет сам
    /// </summary>
    private static bool ValidateWorkspaceContext(ActionExecutingContext context, Guid workspaceId)
    {
        if (context.ActionArguments.TryGetValue("workspaceId", out var queryVal) && queryVal is Guid qId && qId != workspaceId)
        {
            return false;
        }

        if (context.RouteData.Values.TryGetValue("workspaceId", out var routeVal) && routeVal is string rStr
            && Guid.TryParse(rStr, out var rId) && rId != workspaceId)
        {
            return false;
        }

        foreach (var arg in context.ActionArguments.Values)
        {
            if (arg == null) continue;

            var prop = arg.GetType().GetProperty("WorkspaceId");
            if (prop == null || prop.PropertyType != typeof(Guid)) continue;

            var val = (Guid)prop.GetValue(arg)!;
            if (val != Guid.Empty && val != workspaceId)
            {
                return false;
            }
        }

        return true;
    }
}
