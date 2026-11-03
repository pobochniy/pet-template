namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Эндпоинт вне контекста пространства: без <c>X-Workspace-Id</c> и мимо
/// <see cref="PermissionsValidation"/> (спец. admin, 3.5). Признак явный, а не
/// «забыли поставить <c>[AuthorizePermissions]</c>»: <see cref="PermissionsValidation"/>
/// отказывается работать вместе с ним.
///
/// Носители — админские контроллеры: у админского запроса пространства нет вовсе.
/// Личные экраны продукта (аккаунт, список своих пространств) этим признаком не помечаются:
/// они под схемой продукта и просто не ставят <c>[AuthorizePermissions]</c>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class OutsideWorkspace : Attribute;
