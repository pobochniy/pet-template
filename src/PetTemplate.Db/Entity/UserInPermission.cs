using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

public class UserInPermission
{
    public Guid UserId { get; set; }

    public User User { get; set; }

    public PermissionEnum PermissionId { get; set; }
}

public class UserInRoleConfiguration : IEntityTypeConfiguration<UserInPermission>
{
    public void Configure(EntityTypeBuilder<UserInPermission> builder)
    {
        builder
            .HasKey(t => new { t.UserId, RoleId = t.PermissionId });

        builder
            .HasOne(sc => sc.User)
            .WithMany(s => s.UserPermissions)
            .HasForeignKey(sc => sc.UserId);
    }
}