using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

public class User
{
    [Key]
    public Guid Id { get; set; }
    
    public long? TelegramId { get; set; }

    public string? PasswordHash { get; set; }

    public string? SecurityStamp { get; set; }

    public int AccessFailedCount { get; set; }

    public bool EmailConfirmed { get; set; }

    public bool PhoneNumberConfirmed { get; set; }
    
    public virtual Profile? Profile { get; set; }
    
    public virtual ICollection<UserInPermission> UserPermissions { get; set; }

    public User()
    {
        UserPermissions = new List<UserInPermission>();
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder
            .HasIndex(u => u.TelegramId)
            .IsUnique();
        
        builder
            .HasOne(u => u.Profile)
            .WithOne(p => p.User)
            .HasForeignKey<Profile>(p => p.Id);
    }
}