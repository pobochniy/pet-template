using Microsoft.EntityFrameworkCore;

namespace PetTemplate.Db.Entity;

public class ApplicationContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Profile> Profiles { get; set; }
    public DbSet<UserInPermission> UserInPermissions { get; set; }
    

    public ApplicationContext(DbContextOptions options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new UserConfiguration());
        builder.ApplyConfiguration(new ProfileConfiguration());
        builder.ApplyConfiguration(new UserInRoleConfiguration());
    }
}