using Microsoft.AspNetCore.Mvc.Filters;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Доступ к методу внутри пространства из заголовка <c>X-Workspace-Id</c>
/// (спец. authorization, 1.2): нужно хотя бы одно из перечисленных прав в нём.
/// Без аргументов — достаточно быть участником пространства, с любым правом:
/// так закрыт список участников, у которого своего права нет (спец. authorization, 5).
///
/// Проверку делает <see cref="PermissionsValidation"/>: он же сверяет пространство
/// из заголовка с <c>workspaceId</c> в query и <c>WorkspaceId</c> в теле
/// </summary>
public class AuthorizePermissions : Attribute, IFilterFactory
{
    public bool IsReusable => false;
    private PermissionEnum[] Permissions { get; }

    public AuthorizePermissions(params PermissionEnum[] permissions)
    {
        Permissions = permissions;
    }

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var filter = serviceProvider.GetRequiredService<PermissionsValidation>();

        filter.Permissions = Permissions;
        return filter;
    }
}
