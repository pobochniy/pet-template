using Microsoft.AspNetCore.Mvc.Filters;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Доступ к методу внутри пространства из заголовка <c>X-Workspace-Id</c>
/// (спец. authorization, 1.2): нужно хотя бы одно из перечисленных прав в нём.
/// Без аргументов — достаточно быть участником пространства, с любым правом:
/// так закрыт список участников, у которого своего права нет (спец. authorization, 5).
///
/// Проверку делает <see cref="PermissionsValidation"/>: права — фильтром авторизации,
/// до разбора тела; сверку пространства из заголовка с <c>workspaceId</c> в query
/// и <c>WorkspaceId</c> в теле — фильтром действия раньше <c>[ValidateRequest]</c>
/// </summary>
public class AuthorizePermissions : Attribute, IFilterFactory, IOrderedFilter
{
    /// <summary>
    /// Порядок фильтра. MVC сортирует фильтры по дескриптору — по этому атрибуту, а не
    /// по созданному им фильтру. Раньше <c>[ValidateRequest]</c> (порядок 0) на любом уровне:
    /// чужое пространство в теле — <c>403</c>, а не <c>422</c>
    /// </summary>
    public const int FilterOrder = -1000;

    public bool IsReusable => false;

    public int Order => FilterOrder;

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
