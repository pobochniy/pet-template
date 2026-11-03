using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Одноразовый токен аккаунта (спец. accounts, 8.2, 8.4 и 11): ссылка сброса пароля
/// или подтверждения почты. Сам токен — 32 случайных байта — в базу не попадает,
/// только его хеш: утечка таблицы не даёт ни одной рабочей ссылки.
///
/// Выпуск нового подтверждения гасит прежнее (<see cref="RevokedAtUtc"/>), сброс
/// пароля или подтверждение — использованный токен (<see cref="UsedAtUtc"/>)
/// </summary>
public class AccountToken
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public AccountTokenKind Kind { get; set; }

    /// <summary>
    /// SHA-256 токена, hex. <c>null</c> — токен для письма, которое ещё не ушло: сам токен
    /// придумывает обработчик очереди перед отправкой, чтобы в строке очереди
    /// (она тоже в базе) не лежало ничего, чем можно войти (спец. mail, 4)
    /// </summary>
    [MaxLength(64)]
    public string? Hash { get; set; }

    /// <summary>
    /// Адрес письма: подтверждаемый — у подтверждения, подтверждённый в момент запроса —
    /// у сброса. На него и уходит письмо, а не на нынешний адрес профиля
    /// </summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    /// <summary>Погашен выпуском нового токена того же вида, сменой почты или сбросом пароля</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Письмо с токеном ушло — повтор очереди второе не шлёт</summary>
    public DateTime? MailSentAtUtc { get; set; }

    /// <summary>Админ, выдавший ссылку из поддержки (8.5); <c>null</c> — выпущен письмом</summary>
    public Guid? IssuedByUserId { get; set; }

    public virtual User User { get; set; } = null!;
}

public class AccountTokenConfiguration : IEntityTypeConfiguration<AccountToken>
{
    public void Configure(EntityTypeBuilder<AccountToken> builder)
    {
        // Уникальность хеша — по строкам, где он есть: у токена письма, которое ещё
        // не ушло, хеша нет, а NULL с NULL в уникальном индексе Postgres не совпадают
        builder.HasIndex(x => x.Hash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.Kind });

        // Токены — личное: уходят вместе с аккаунтом (раздел 10)
        builder
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Журнальная ссылка на админа: удалён админ — токен остаётся без автора
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.IssuedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
