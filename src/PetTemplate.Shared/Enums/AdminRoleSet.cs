namespace PetTemplate.Shared.Enums;

/// <summary>
/// Набор ролей одним числом (<see cref="AdminRole"/> флаговый ради приглашения, спец.
/// admin, 3.4) и обратно — роли по одной, как они лежат в <c>UserInAdminRoles</c>
/// </summary>
public static class AdminRoleSet
{
    /// <summary>Все роли, какие есть, по возрастанию номера</summary>
    public static readonly AdminRole[] All = Enum.GetValues<AdminRole>().Where(x => x != AdminRole.None).ToArray();

    public static AdminRole[] Split(AdminRole roles) => All.Where(x => roles.HasFlag(x)).ToArray();

    public static AdminRole Join(IEnumerable<AdminRole> roles) =>
        roles.Aggregate(AdminRole.None, (set, role) => set | role);

    /// <summary>Ровно одна известная роль — не склейка и не номер из будущего</summary>
    public static bool IsSingle(AdminRole role) => Array.IndexOf(All, role) >= 0;
}
