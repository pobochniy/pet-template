using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

public class Profile
{
    [Key]
    public Guid Id { get; set; }

    [MinLength(2)]
    [MaxLength(20)]
    public string? UserName { get; set; }
    
    [MaxLength(255)]
    public int? AvatarId { get; set; }
    
    [MaxLength(3000)]
    public string? Comment { get; set; }
    
    public bool IsAdult { get; set; }
    
    public bool HasAcceptedTerms { get; set; }

    [MaxLength(100)]
    public string? WalletAddress { get; set; }

    public DateTime? WalletConnectedAt { get; set; }

    public virtual User? User { get; set; }
}

public class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder
            .HasIndex(u => u.UserName)
            .IsUnique();

        builder
            .Property(u => u.Comment)
            .HasColumnType("text");

        builder
            .Property(u => u.WalletAddress)
            .HasMaxLength(100);
    }
}