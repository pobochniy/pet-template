using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PetTemplate.Api.Middlewares;

namespace PetTemplate.Tests.Features.Workspace;

/// <summary>
/// Разметка методов, читающих пространство из параметров (спец. authorization, 5).
/// <c>[AuthorizePermissions]</c> сверяет <c>workspaceId</c> в маршруте, query и теле
/// с заголовком; метод без него доверяет клиенту, и забытый атрибут не падает ни одним
/// запросом — поэтому стережёт сборка, а не сценарий
/// </summary>
public class WorkspaceWiringTests
{
    private const string WorkspaceIdName = "workspaceId";

    /// <summary>
    /// Без <c>[AuthorizePermissions]</c> законно: основное и выход — сервис сам сверяет
    /// членство (спец. authorization, 7). Новый метод дописывается сюда явно
    /// </summary>
    private static readonly HashSet<string> TrustedActions =
    [
        "WorkspaceController.SetPrimary",
        "WorkspaceController.Leave"
    ];

    [Fact]
    public void FindsActionsWithWorkspaceId()
    {
        Assert.Contains(ProductActions(), ReadsWorkspaceId);
    }

    [Fact]
    public void GuardsEveryActionReadingWorkspaceId()
    {
        var unguarded = ProductActions()
            .Where(ReadsWorkspaceId)
            .Where(x => !IsGuarded(x))
            .Select(Name)
            .Where(x => !TrustedActions.Contains(x))
            .ToList();

        Assert.Empty(unguarded);
    }

    /// <summary>Белый список без мёртвых строк: метод есть, читает пространство и без атрибута</summary>
    [Fact]
    public void TrustedActionsAreStillUnguarded()
    {
        var unguarded = ProductActions()
            .Where(x => ReadsWorkspaceId(x) && !IsGuarded(x))
            .Select(Name)
            .ToHashSet();

        Assert.All(TrustedActions, x => Assert.Contains(x, unguarded));
    }

    /// <summary>
    /// Контроллеры продукта. Админка (<c>[OutsideWorkspace]</c>) пространства не знает
    /// и видит все по своей роли — её разметку стережёт <c>AdminWiringTests</c>
    /// </summary>
    private static IEnumerable<(Type Controller, MethodInfo Method)> ProductActions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(x => x.IsSubclassOf(typeof(ControllerBase)) && !x.IsAbstract)
            .Where(x => x.GetCustomAttribute<OutsideWorkspace>() == null)
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(x => x.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(method => (controller, method)));

    private static bool IsGuarded((Type Controller, MethodInfo Method) action) =>
        action.Method.GetCustomAttribute<AuthorizePermissions>() != null
        || action.Controller.GetCustomAttribute<AuthorizePermissions>() != null;

    /// <summary>Параметр <c>workspaceId</c>, он же в шаблоне маршрута или свойство <c>WorkspaceId</c> модели</summary>
    private static bool ReadsWorkspaceId((Type Controller, MethodInfo Method) action)
    {
        var templates = action.Method.GetCustomAttributes<HttpMethodAttribute>().Select(x => x.Template)
            .Concat(action.Method.GetCustomAttributes<RouteAttribute>().Select(x => x.Template))
            .Concat(action.Controller.GetCustomAttributes<RouteAttribute>().Select(x => x.Template));
        if (templates.Any(x => x?.Contains("{" + WorkspaceIdName, StringComparison.OrdinalIgnoreCase) == true))
            return true;

        return action.Method.GetParameters().Any(x =>
            string.Equals(x.Name, WorkspaceIdName, StringComparison.OrdinalIgnoreCase)
            || HasWorkspaceIdProperty(x.ParameterType));
    }

    private static bool HasWorkspaceIdProperty(Type type)
    {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid) || type == typeof(CancellationToken))
            return false;

        return type.GetProperty(WorkspaceIdName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null;
    }

    private static string Name((Type Controller, MethodInfo Method) action) =>
        $"{action.Controller.Name}.{action.Method.Name}";
}
