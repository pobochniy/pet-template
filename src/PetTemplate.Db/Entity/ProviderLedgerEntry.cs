using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Наш счёт у поставщика (спец. assistant, 13.5; admin, 14): платежи ему и факт расхода
/// за период. Остаток — платежи минус факт, и следить за ним надо раньше, чем за лимитами
/// пространств: кончится он — ассистент встанет у всех разом.
///
/// Строка не меняется после записи. Правка факта — новая запись
/// <see cref="ProviderLedgerKind.ActualSpend"/> с тем же периодом: действует последняя,
/// а прежняя остаётся следом того, что вводили раньше
/// </summary>
public class ProviderLedgerEntry
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Код поставщика — тот же, что в <see cref="AiModel.Provider"/></summary>
    [MaxLength(64)]
    public required string Provider { get; set; }

    public ProviderLedgerKind Kind { get; set; }

    /// <summary>
    /// Сумма в валюте прайса справочника. У платежа со знаком: минус — исправление
    /// ошибочно записанного платежа, раз строки не правятся
    /// </summary>
    [Precision(18, 6)]
    public decimal Amount { get; set; }

    /// <summary>Начало периода факта расхода, включительно. У платежа пусто</summary>
    public DateTime? PeriodFrom { get; set; }

    /// <summary>Конец периода факта расхода, не включительно. У платежа пусто</summary>
    public DateTime? PeriodTo { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Кто записал. Админ может уйти из системы, а запись о деньгах — нет: ссылка
    /// пустеет (спец. accounts, раздел 10)
    /// </summary>
    public Guid? ActorUserId { get; set; }
}

public class ProviderLedgerEntryConfiguration : IEntityTypeConfiguration<ProviderLedgerEntry>
{
    public void Configure(EntityTypeBuilder<ProviderLedgerEntry> builder)
    {
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Сводка читает счёт поставщика целиком: записей — единицы в месяц
        builder.HasIndex(x => new { x.Provider, x.Kind });
    }
}
