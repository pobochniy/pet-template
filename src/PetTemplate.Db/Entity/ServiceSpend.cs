using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Служебный расход (спец. assistant, 13.5): обращение к поставщику не ходом человека —
/// компакция треда (12.4) и расшифровка голоса (раздел 6). Поставщику он уходит
/// в тот же счёт, что и ходы, и без этих строк расчёт разошёлся бы с фактом ровно на них.
/// В лимит пространства идёт только расшифровка: её просил человек (13.6).
///
/// Строки не чистятся и внешних ключей на пространство не держат: это деньги,
/// ушедшие поставщику, и счёт у него должен видеть их и после сноса пространства
/// </summary>
public class ServiceSpend
{
    [Key]
    public Guid Id { get; set; }

    public ServiceSpendKind Kind { get; set; }

    /// <summary>
    /// Модель справочника, которая отвечала, — по её цене идёт расчёт. Пусто
    /// у расшифровки: модель распознавания в справочнике чата не стоит (её не выбирают
    /// по приоритету и не размыкают), цена у неё своя — за минуту
    /// </summary>
    public Guid? ModelId { get; set; }

    /// <summary>
    /// Код поставщика (<c>AssistantOptions.Providers</c>), в чей счёт ушёл вызов:
    /// по нему сверяется остаток у поставщика (<c>ProviderLedger.Estimate</c>) — и у строки
    /// без модели справочника тоже
    /// </summary>
    [MaxLength(64)]
    public required string Provider { get; set; }

    /// <summary>
    /// Идентификатор модели у поставщика — у расшифровки, где строки справочника нет;
    /// у остальных повторяет <c>ai_models.key</c> на момент вызова
    /// </summary>
    [MaxLength(256)]
    public required string ModelKey { get; set; }

    /// <summary>Все входящие, как их отдаёт поставщик: кешированные — внутри</summary>
    public long TokensIn { get; set; }

    public long TokensCached { get; set; }

    public long TokensOut { get; set; }

    /// <summary>
    /// Секунды записи — у расшифровки: по ним считается её цена
    /// (<c>Transcription.CostPerMinute</c>). У остальных видов — ноль
    /// </summary>
    public int Seconds { get; set; }

    /// <summary>
    /// <c>RunBilling.Cost</c> по цене модели на момент вызова: правка прайса
    /// прошлое не переписывает
    /// </summary>
    [Precision(18, 6)]
    public decimal Cost { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Чей это тред (компакция) или чей лимит съеден (расшифровка, 13.6)
    /// </summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>
    /// Кто спросил — у расшифровки, как у <c>RunSpend.UserId</c>: кто сколько потратил
    /// внутри пространства. Внешнего ключа нет — строка переживает удаление аккаунта
    /// </summary>
    public Guid? UserId { get; set; }

    public virtual AiModel? Model { get; set; }
}

public class ServiceSpendConfiguration : IEntityTypeConfiguration<ServiceSpend>
{
    public void Configure(EntityTypeBuilder<ServiceSpend> builder)
    {
        // Сводка за период
        builder.HasIndex(x => x.CreatedUtc);

        // Лимит пространства: расшифровки за сутки и за месяц (13.6)
        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedUtc });

        // Модель не удаляется, а выключается (16.2)
        builder
            .HasOne(x => x.Model)
            .WithMany()
            .HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
