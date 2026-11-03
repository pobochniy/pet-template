using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using PetTemplate.Api.Middlewares;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Разметка контроллеров <c>Admin*</c> (спец. admin, 3.5). Забытый атрибут здесь
/// не падает ни одним запросом — эндпоинт просто молча открыт, — поэтому стережёт
/// его сборка, а не сценарий
/// </summary>
public class AdminWiringTests
{
    /// <summary>
    /// Вне <c>[AuthorizeAdmin]</c> живут вход, страница приглашения и его принятие (3.4)
    /// и подзапрос шлюза <c>Verify</c> (3.3). Новый открытый метод дописывается сюда явно
    /// </summary>
    private static readonly HashSet<string> OpenActions =
    [
        "AdminAuthController.Login",
        "AdminAuthController.PreviewInvitation",
        "AdminAuthController.AcceptInvitation",
        "AdminAuthController.Verify"
    ];

    [Fact]
    public void FindsAdminControllers()
    {
        Assert.NotEmpty(AdminActions());
    }

    [Fact]
    public void GuardsEveryAdminAction()
    {
        var unguarded = AdminActions()
            .Where(x => x.Method.GetCustomAttribute<AuthorizeAdmin>() == null
                        && x.Controller.GetCustomAttribute<AuthorizeAdmin>() == null)
            .Select(x => $"{x.Controller.Name}.{x.Method.Name}")
            .Where(x => !OpenActions.Contains(x))
            .ToList();

        Assert.Empty(unguarded);
    }

    [Fact]
    public void KeepsAdminOutsideWorkspace()
    {
        var inWorkspace = AdminControllers()
            .Where(x => x.GetCustomAttribute<OutsideWorkspace>() == null)
            .Select(x => x.Name)
            .ToList();

        Assert.Empty(inWorkspace);
    }

    /// <summary>
    /// Ни прав пространства, ни <c>[Authorize]</c> схемы продукта: у админки своя схема,
    /// а пространства у её запросов нет
    /// </summary>
    [Fact]
    public void KeepsProductAuthorizationOffAdmin()
    {
        var mixed = AdminActions()
            .Where(x => x.Method.GetCustomAttribute<AuthorizePermissions>() != null
                        || x.Controller.GetCustomAttribute<AuthorizePermissions>() != null
                        || x.Method.GetCustomAttribute<AuthorizeAttribute>() != null
                        || x.Controller.GetCustomAttribute<AuthorizeAttribute>() != null)
            .Select(x => $"{x.Controller.Name}.{x.Method.Name}")
            .ToList();

        Assert.Empty(mixed);
    }

    /// <summary>
    /// <c>[OutsideWorkspace]</c> вместе с <c>[AuthorizePermissions]</c> — ошибка разметки: проверка
    /// прав не выбирает молча ни «требовать пространство у админки», ни «пускать без проверки»
    /// </summary>
    [Fact]
    public async Task PermissionsRefuseEndpointOutsideWorkspace()
    {
        var descriptor = new ActionDescriptor { EndpointMetadata = [new OutsideWorkspace()], DisplayName = "Mixed" };
        var context = new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor),
            new List<IFilterMetadata>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => new PermissionsValidation(null!).OnAuthorizationAsync(context));
    }

    /// <summary>
    /// «Аккаунты» — под ролью <c>Accounts</c> целиком, и чтение тоже (спец. accounts, 8.5):
    /// карточка чужого аккаунта — не экран «для любого админа»
    /// </summary>
    [Fact]
    public void GuardsAccountsWithAccountsRole()
    {
        var guard = typeof(PetTemplate.Api.Controllers.AdminAccountsController).GetCustomAttribute<AuthorizeAdmin>();

        Assert.Equal([PetTemplate.Shared.Enums.AdminRole.Accounts], guard!.Roles);
    }

    private static IEnumerable<Type> AdminControllers() =>
        typeof(Program).Assembly.GetTypes()
            .Where(x => x.IsSubclassOf(typeof(ControllerBase)))
            .Where(x => x.GetCustomAttribute<RouteAttribute>()?.Template
                .StartsWith("api/Admin", StringComparison.OrdinalIgnoreCase) == true);

    private static IEnumerable<(Type Controller, MethodInfo Method)> AdminActions() =>
        AdminControllers().SelectMany(controller => controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => x.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Select(method => (controller, method)));
}
