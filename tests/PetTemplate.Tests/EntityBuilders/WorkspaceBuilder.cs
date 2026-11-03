using PetTemplate.Db.Entity;

namespace PetTemplate.Tests.EntityBuilders;

/// <summary>
/// Пространство для подготовки базы. Права участников в нём выдаёт
/// <see cref="UserBuilder.WithPermissions(Guid, PermissionEnum[])"/>
/// </summary>
public class WorkspaceBuilder
{
    private readonly Workspace _workspace;

    public WorkspaceBuilder(string name = "Рога и Копыта", Guid? id = null)
    {
        _workspace = new Workspace
        {
            Id = id ?? Guid.CreateVersion7(),
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Дата создания — от неё считается активность пустого пространства (спец. authorization, 8)
    /// </summary>
    public WorkspaceBuilder CreatedAt(DateTime utc)
    {
        _workspace.CreatedAt = utc;
        return this;
    }

    public Workspace Please()
    {
        return _workspace;
    }
}
