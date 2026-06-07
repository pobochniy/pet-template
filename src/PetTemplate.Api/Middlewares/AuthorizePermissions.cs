using Microsoft.AspNetCore.Mvc.Filters;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Middlewares;

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