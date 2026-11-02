using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Приглашение админа (спец. admin, 3.4) — нарочно устроено как
/// <see cref="WorkspaceInvitation"/>: одноразовая ссылка, токен v4, срок, отзыв
/// непринятого. Принятое дописывает строки <see cref="UserInAdminRole"/>, само
/// членства не хранит.
///
/// Выдавший, принявший и отозвавший могут уйти из системы — ссылки на них пустеют,
/// а строка доживает до чистки (21 день). В базе — <c>admin_invitations</c>
/// </summary>
public class AdminInvitation
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// SHA-256 токена из ссылки (<see cref="HashOf"/>), сам токен не хранится: он
    /// уходит один раз, в ответе на выдачу. Иначе любой с <c>AdminManagement</c> взял
    /// бы из списка чужую ссылку с ролью, которой у него нет, и принял её сам.
    /// Соль не нужна: токен — <c>Guid</c> v4, 122 случайных бита, перебирать нечего.
    /// Отдельной колонкой (<c>bytea</c>): <see cref="Id"/> ездит в списке и в отзыве
    /// </summary>
    [MaxLength(32)]
    public byte[] TokenHash { get; set; } = null!;

    /// <summary>Набор ролей одним числом — ради этого <see cref="AdminRole"/> флаговый</summary>
    public AdminRole Roles { get; set; }

    /// <summary>
    /// <see cref="AdminInvitationStatus.Expired"/> сюда не пишется — он считается по
    /// <see cref="ExpiresUtc"/>. Токен конкуренции: принятие уходит условным
    /// <c>UPDATE … WHERE status = Pending</c>, и второй из двух одновременных
    /// запросов не меняет ни одной строки
    /// </summary>
    public AdminInvitationStatus Status { get; set; }

    /// <summary><see cref="CreatedUtc"/> + <c>AdminInviteTtl</c> (3 дня)</summary>
    public DateTime ExpiresUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? AcceptedByUserId { get; set; }

    /// <summary>Кто отозвал (3.5: всё, что меняет данные, пишет, кто) — у отозванного</summary>
    public Guid? RevokedByUserId { get; set; }

    /// <summary>От него считает чистка: 21 день в любом статусе</summary>
    public DateTime CreatedUtc { get; set; }

    public static byte[] HashOf(Guid token) => SHA256.HashData(token.ToByteArray());
}

public class AdminInvitationConfiguration : IEntityTypeConfiguration<AdminInvitation>
{
    public void Configure(EntityTypeBuilder<AdminInvitation> builder)
    {
        // Приглашение переживает выдавшего, принявшего и отозвавшего — ссылка пустеет
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AcceptedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RevokedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // По токену ходит страница приглашения — единственный путь к строке снаружи
        builder.HasIndex(x => x.TokenHash).IsUnique();

        // Под суточную чистку
        builder.HasIndex(x => x.CreatedUtc);

        // Одно поле — токен конкуренции, а не вся строка (`xmin`) — гонка здесь одна,
        // за статус (спец. admin, 5; infrastructure, 3.1)
        builder.Property(x => x.Status).IsConcurrencyToken();
    }
}
