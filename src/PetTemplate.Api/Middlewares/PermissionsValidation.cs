using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Middlewares;

public class PermissionsValidation : Attribute, IActionFilter
{
    public PermissionEnum[] Permissions { get; set; }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.User.Identity.IsAuthenticated)
        {
            context.Result = new StatusCodeResult(401);
            return;
        }

        var userPermissions = context.HttpContext.User.Claims
            .Where(x => x.Type == ClaimTypes.Role)
            .Select(x => (PermissionEnum)int.Parse(x.Value))
            .ToArray();

        var isAllowed = Permissions.Any(el => userPermissions.Contains(el));

        if (!isAllowed) context.Result = new StatusCodeResult(403);
    }
}