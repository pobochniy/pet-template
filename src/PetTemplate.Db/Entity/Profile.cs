using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Db.Entity;

public class Profile
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Логин — обязателен у каждого аккаунта (спец. accounts, раздел 4): по нему люди
    /// в workspace узнают друг друга. Хранится как ввёл человек; уникальность — без учёта
    /// регистра, её даёт тип колонки (<c>citext</c>), а не отдельный нормализованный столбец.
    ///
    /// Правила ввода — <see cref="UserNameRule"/>. Здесь их нет намеренно: существующие
    /// логины, которые правилам не отвечают, остаются как есть (4.1). Длину <c>citext</c>
    /// не держит — её держат DTO на входе
    /// </summary>
    [MaxLength(UserNameRule.MaxLength)]
    public string UserName { get; set; } = null!;

    /// <summary>
    /// Логин подставлен сервером при регистрации через Telegram или VK (4.2), а не
    /// выбран человеком: пока так, профиль предлагает его сменить. Сбрасывает первая
    /// смена логина
    /// </summary>
    public bool UserNameIsGenerated { get; set; }

    public int? AvatarId { get; set; }

    /// <summary>
    /// Почта — она же логин для входа; до 255 символов, предел держат DTO (8.4)
    /// </summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// <see cref="Email"/> подтверждена переходом по ссылке из письма (спец. accounts, 8.4).
    /// Рядом с адресом, а не в <c>User</c>: смена почты сбрасывает признак
    /// </summary>
    public bool EmailConfirmed { get; set; }

    [MaxLength(255)]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Язык интерфейса и писем (спец. accounts, 8.4; mail, 3) — код из
    /// <see cref="LanguageConstants.Supported"/>. Пишется при регистрации и сменой языка
    /// в шапке; на нём уходят письма
    /// </summary>
    [MaxLength(LanguageConstants.CodeMaxLength)]
    public string Language { get; set; } = LanguageConstants.Default;
    
    [MaxLength(3000)]
    public string? Comment { get; set; }
    
    public bool IsAdult { get; set; }
    
    public bool HasAcceptedTerms { get; set; }

    /// <summary>
    /// Пространство, которое открывается по умолчанию при входе (спец. authorization, 7.1).
    /// К членству отношения не имеет — это лишь указатель: членство живёт
    /// в <see cref="UserInPermission"/>, и то, что указатель ведёт в своё пространство,
    /// модель не гарантирует — проверяет <c>WorkspaceService.SetPrimary</c>.
    /// Каскада нет: пространство удаляет выход последнего или фоновая чистка, и обе
    /// обнуляют указатель сами, до удаления (<c>WorkspaceService.PurgeContents</c>)
    /// </summary>
    public Guid? PrimaryWorkspaceId { get; set; }

    public virtual Workspace? PrimaryWorkspace { get; set; }

    public virtual User? User { get; set; }
}

public class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        // citext — уникальность и поиск без учёта регистра: Masha и masha один логин,
        // одна почта (спец. infrastructure, 3.1). Длину у citext база не держит —
        // её проверяет валидация DTO
        builder
            .Property(u => u.UserName)
            .HasColumnType("citext");

        builder
            .Property(u => u.Email)
            .HasColumnType("citext");

        builder
            .HasIndex(u => u.UserName)
            .IsUnique();

        // Почта — логин для входа, поэтому у одного аккаунта (спец. accounts, 8.4).
        // Пустая не мешает: в уникальном индексе Postgres NULL с NULL не совпадают
        builder
            .HasIndex(u => u.Email)
            .IsUnique();

        builder
            .Property(u => u.Comment)
            .HasColumnType("text");

        builder
            .HasOne(u => u.PrimaryWorkspace)
            .WithMany()
            .HasForeignKey(u => u.PrimaryWorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}