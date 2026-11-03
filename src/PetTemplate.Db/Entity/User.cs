using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

public class User
{
    [Key]
    public Guid Id { get; set; }
    
    /// <summary>
    /// Привязанный VK — <c>user_id</c> VK ID на сайте и <c>vk_user_id</c> в mini-app (спец.
    /// accounts, разделы 3, 5.3 и 5.6); как <see cref="TelegramId"/>. Токен доступа VK
    /// не хранится (5.3)
    /// </summary>
    public long? VkId { get; set; }

    /// <summary>
    /// Привязанный Telegram — claim <c>id</c> из <c>id_token</c> окна входа и <c>user.id</c>
    /// из <c>initData</c> mini-app: один и тот же числовой id (спец. accounts, разделы 3 и 5.2).
    /// Один Telegram — один аккаунт: уникальный индекс, <c>null</c> — не привязан
    /// </summary>
    public long? TelegramId { get; set; }

    /// <summary>
    /// Когда Telegram привязан к уже существующему аккаунту — кнопкой «Привязать», развилкой
    /// mini-app или слиянием (спец. accounts, 8.3). Пока не прошло
    /// <c>AccountOptions.RecentLinkWindow</c>, свежий вход им не задаёт пароль без текущего
    /// и не подтверждает владение (8.6): иначе украденная сессия привязывала бы свой Telegram
    /// и через него меняла пароль. <c>null</c> — аккаунт заведён этим входом: такому входу
    /// доверие полное
    /// </summary>
    public DateTime? TelegramLinkedAtUtc { get; set; }

    /// <summary>То же для VK — как <see cref="TelegramLinkedAtUtc"/></summary>
    public DateTime? VkLinkedAtUtc { get; set; }

    /// <summary>
    /// Хеш пароля (<c>PasswordHasher</c>, спец. accounts, 8.1). <c>null</c> — пароля нет:
    /// аккаунт, пришедший из Telegram или VK, входит только через площадку, пока не задаст
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Соль прежней схемы хеша (SHA-256 от «соль#пароль»). Новая схема держит соль внутри
    /// <see cref="PasswordHash"/>, поэтому при пересчёте хеша поле обнуляется
    /// </summary>
    public string? SecurityStamp { get; set; }

    /// <summary>Когда заведён аккаунт (спец. accounts, 11): карточка поддержки и экран слияния</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Токены, выпущенные раньше этой отметки, не действуют (спец. accounts, 8.1 и 11):
    /// смена и сброс пароля завершают остальные сессии. <c>null</c> — ничего
    /// не отозвано. Отметка — целые секунды: время выпуска в JWT (<c>iat</c>) тоже
    /// в секундах, и токен, выданный в ту же секунду после отзыва, не должен отбиться.
    /// Ставит её <c>SessionGuard.Revoke</c>, сверяет — проверка каждого запроса
    /// </summary>
    public DateTime? TokensValidAfterUtc { get; set; }

    /// <summary>
    /// Когда аккаунт последний раз вошёл (спец. accounts, 8.5): по ней поддержка
    /// проверяет владельца — «когда заходил»
    /// </summary>
    public DateTime? LastLoginAtUtc { get; set; }
    
    public virtual Profile? Profile { get; set; }
    
    public virtual ICollection<UserInPermission> UserPermissions { get; set; }

    /// <summary>Админские роли (спец. admin, 3.1): нет строк — не админ</summary>
    public virtual ICollection<UserInAdminRole> AdminRoles { get; set; }

    public User()
    {
        UserPermissions = new List<UserInPermission>();
        AdminRoles = new List<UserInAdminRole>();
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // Один вход площадки — один аккаунт (спец. accounts, раздел 3). Индекс держит и гонку
        // двух одновременных первых входов (4.2): второй упирается в него и входит в первый
        builder
            .HasIndex(u => u.TelegramId)
            .IsUnique();
        
        builder
            .HasIndex(u => u.VkId)
            .IsUnique();
        
        builder
            .HasOne(u => u.Profile)
            .WithOne(p => p.User)
            .HasForeignKey<Profile>(p => p.Id);
    }
}