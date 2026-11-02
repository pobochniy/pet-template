using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Свой лимит расхода ассистента у пространства (спец. assistant, 13.6) — вместо
/// умолчания из конфигурации (<c>SpendOptions.DefaultLimit</c>). Строки нет — действует
/// умолчание; строка есть — действует она целиком, и пустое поле в ней значит
/// «без потолка», а не «как по умолчанию»: иначе «снять потолок для этого пространства»
/// записать было бы нечем. Правится в админке (спец. admin, 14).
///
/// Лимит общий на всех участников пространства: считается их расход вместе
/// </summary>
public class WorkspaceAssistantLimit
{
    [Key]
    public Guid WorkspaceId { get; set; }

    /// <summary>Токенов (входящие и исходящие вместе) за сутки по UTC</summary>
    public long? TokensPerDay { get; set; }

    /// <summary>Токенов за календарный месяц по UTC</summary>
    public long? TokensPerMonth { get; set; }

    /// <summary>Денег за сутки по UTC, в валюте прайса справочника моделей</summary>
    [Precision(18, 6)]
    public decimal? CostPerDay { get; set; }

    /// <summary>Денег за календарный месяц по UTC</summary>
    [Precision(18, 6)]
    public decimal? CostPerMonth { get; set; }

    public DateTime UpdatedUtc { get; set; }

    /// <summary>Кто правил последним; ушёл админ — ссылка пустеет</summary>
    public Guid? UpdatedByUserId { get; set; }

    public virtual Workspace Workspace { get; set; } = null!;
}

public class WorkspaceAssistantLimitConfiguration : IEntityTypeConfiguration<WorkspaceAssistantLimit>
{
    public void Configure(EntityTypeBuilder<WorkspaceAssistantLimit> builder)
    {
        // Как у содержимого пространства: каскада от него нет, строку убирает
        // WorkspaceService.PurgeContents до самого пространства
        builder
            .HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    /// <summary>
    /// Имя ключа — по нему <c>DbErrors.IsDuplicate</c> узнаёт, что первую строку
    /// пространству одновременно завели два админа
    /// </summary>
    public const string Key = "pk_workspace_assistant_limits";
}
