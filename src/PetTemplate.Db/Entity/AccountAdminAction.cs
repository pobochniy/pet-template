using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Журнал работы админа с чужим аккаунтом (спец. accounts, 8.5 и 11): кто, кому, когда,
/// причина. Ссылка сброса — это вход в чужой аккаунт, и разбирать спор «у меня увели
/// аккаунт» без этого журнала нечем.
///
/// Аккаунт, которому выдали ссылку или которого удалили, журнал переживает: его id
/// и логин лежат копией, без внешнего ключа — после удаления ссылаться не на что.
/// В базе — <c>account_admin_actions</c>
/// </summary>
public class AccountAdminAction
{
    /// <summary>Длина причины в журнале (спец. admin, 5)</summary>
    public const int ReasonMaxLength = 500;

    [Key]
    public Guid Id { get; set; }

    public AccountAdminActionKind Kind { get; set; }

    /// <summary>Админ; удалён — пусто</summary>
    public Guid? AdminUserId { get; set; }

    public Guid TargetUserId { get; set; }

    /// <summary>Логин цели на момент действия — копией: журнал ищется по нему и после удаления</summary>
    [MaxLength(UserNameRule.MaxLength)]
    public string TargetUserName { get; set; } = string.Empty;

    [MaxLength(ReasonMaxLength)]
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}

public class AccountAdminActionConfiguration : IEntityTypeConfiguration<AccountAdminAction>
{
    public void Configure(EntityTypeBuilder<AccountAdminAction> builder)
    {
        builder.HasIndex(x => x.TargetUserId);
        builder.HasIndex(x => x.CreatedUtc);

        // Журнал переживает админа — ссылка пустеет (спец. accounts, раздел 10)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AdminUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
