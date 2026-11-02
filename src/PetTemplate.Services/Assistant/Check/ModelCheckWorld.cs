using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Assistant.Check;

/// <summary>
/// Пространство для фразы сверки (спец. admin, 15.3): люди с именами, которые встречаются
/// во фразах набора, и полные права у автора — сверять надо разбор фразы, а не разрешения.
/// Живёт оно в базе в памяти и с ней же умирает.
///
/// В ядре здесь только люди: предметных записей у шаблона нет. Раздел проекта со своими
/// инструментами сеет сюда то, о чём говорят его фразы
/// </summary>
public static class ModelCheckWorld
{
    public static (Guid UserId, Guid WorkspaceId) Seed(ApplicationContext context)
    {
        var workspaceId = Guid.CreateVersion7();
        context.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "Рога и Копыта",
            CreatedAt = DateTime.UtcNow
        });

        var oleg = Person(context, workspaceId, "Олег", all: true);
        Person(context, workspaceId, "Илья Петров", all: false);
        Person(context, workspaceId, "Настя", all: false);

        context.SaveChanges();

        return (oleg, workspaceId);
    }

    private static Guid Person(ApplicationContext context, Guid workspaceId, string name, bool all)
    {
        var id = Guid.CreateVersion7();
        var user = new User
        {
            Id = id,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow
        };

        var permissions = all
            ? Enum.GetValues<PermissionEnum>().Where(x => x != PermissionEnum.None)
            : [PermissionEnum.WorkspaceMemberRead];

        foreach (var permission in permissions)
            user.UserPermissions.Add(new UserInPermission
            {
                WorkspaceId = workspaceId,
                UserId = id,
                PermissionId = permission
            });

        context.Profiles.Add(new Profile { Id = id, UserName = name, User = user });

        return id;
    }
}
