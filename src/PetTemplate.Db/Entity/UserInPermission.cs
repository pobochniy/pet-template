using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Право пользователя в пространстве (спец. authorization, 1 и 4). Глобальных прав
/// без пространства нет, а набор строк с одним <see cref="WorkspaceId"/> — это и есть
/// членство: отдельной сущности участника нет
/// </summary>
public class UserInPermission
{
    public Guid WorkspaceId { get; set; }

    public Workspace Workspace { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public PermissionEnum PermissionId { get; set; }
}

public class UserInPermissionConfiguration : IEntityTypeConfiguration<UserInPermission>
{
    public void Configure(EntityTypeBuilder<UserInPermission> builder)
    {
        builder
            .HasKey(t => new { t.WorkspaceId, t.UserId, t.PermissionId });

        // Права одного человека по всем пространствам — вход и список своих пространств
        builder.HasIndex(t => t.UserId);

        builder
            .HasOne(sc => sc.User)
            .WithMany(s => s.UserPermissions)
            .HasForeignKey(sc => sc.UserId)
            // Членство уходит вместе с аккаунтом; общие пространства до этого передают
            // права оставшимся (спец. authorization, 8; удаление аккаунта — AccountDeletion)
            .OnDelete(DeleteBehavior.Cascade);

        // Каскада от пространства нет: его удаление идёт явным порядком
        // (WorkspaceService.PurgeContents), а к тому времени прав в нём уже нет
        builder
            .HasOne(sc => sc.Workspace)
            .WithMany(s => s.UserPermissions)
            .HasForeignKey(sc => sc.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
