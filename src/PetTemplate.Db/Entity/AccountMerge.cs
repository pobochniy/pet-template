using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Журнал слияний (спец. accounts, 7.4 и 11): остающийся, удалённый, когда, чем доказано
/// владение вторым. Разбирать спор «у меня пропал аккаунт» без него нечем. Удалённый лежит
/// копией — id и логин, без внешнего ключа: его строки больше нет
/// </summary>
public class AccountMerge
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Остающийся; удалят и его — ссылка пустеет</summary>
    public Guid? KeptUserId { get; set; }

    /// <summary>
    /// Логин остающегося на момент слияния — копией: журнал ищется по нему и после того,
    /// как остающийся сменит логин или сам будет удалён
    /// </summary>
    [MaxLength(UserNameRule.MaxLength)]
    public string KeptUserName { get; set; } = string.Empty;

    public Guid RemovedUserId { get; set; }

    [MaxLength(UserNameRule.MaxLength)]
    public string RemovedUserName { get; set; } = string.Empty;

    /// <summary>Чем доказано владение вторым: <c>password</c>, <c>telegram</c>, <c>vk</c></summary>
    [MaxLength(16)]
    public string Platform { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}

public class AccountMergeConfiguration : IEntityTypeConfiguration<AccountMerge>
{
    public void Configure(EntityTypeBuilder<AccountMerge> builder)
    {
        builder.HasIndex(x => x.RemovedUserId);

        // Журнальная ссылка: удалён и остающийся — запись остаётся без него (раздел 10)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.KeptUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
