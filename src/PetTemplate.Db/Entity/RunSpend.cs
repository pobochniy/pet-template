using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Расход хода (спец. assistant, 13.5): одна строка на прогон с токенами, записанная
/// при его завершении, — по ней считаются лимит пространства (13.6) и сводка «Расход»
/// в админке. В Lad на этом месте стояло списание с кошелька; баланса в шаблоне нет,
/// и деньги ни у кого не снимаются — строка только учитывает.
///
/// Строки не правятся и не чистятся, внешних ключей на прогон, пространство
/// и человека не держат: это деньги, ушедшие поставщику, и счёт у него должен видеть
/// их и после «Очистить», удаления аккаунта и сноса пространства. Прогон уходит
/// с пространством, а его расход остаётся в итогах по моделям и поставщикам
/// </summary>
public class RunSpend
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Прогон — уникален: повтор учёта того же хода ловит индекс, а не проверка
    /// </summary>
    public Guid RunId { get; set; }

    /// <summary>Чей лимит съеден — лимит считается по пространству целиком (13.6)</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>Кто спросил: кто сколько потратил внутри пространства, видно в админке</summary>
    public Guid UserId { get; set; }

    /// <summary>Модель, которой шёл ход, — по её цене и её поставщику идёт расчёт</summary>
    public Guid ModelId { get; set; }

    /// <summary>Все входящие, как их отдаёт поставщик: кешированные — внутри</summary>
    public long TokensIn { get; set; }

    public long TokensCached { get; set; }

    public long TokensOut { get; set; }

    /// <summary>
    /// Стоимость по цене модели на момент завершения хода, в валюте прайса
    /// справочника: правка прайса прошлое не переписывает
    /// </summary>
    [Precision(18, 6)]
    public decimal Cost { get; set; }

    public DateTime CreatedUtc { get; set; }

    public virtual AiModel Model { get; set; } = null!;
}

public class RunSpendConfiguration : IEntityTypeConfiguration<RunSpend>
{
    public void Configure(EntityTypeBuilder<RunSpend> builder)
    {
        builder.HasIndex(x => x.RunId).IsUnique();

        // Лимит пространства: расход за сутки и за месяц (13.6)
        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedUtc });

        // Сводка за период
        builder.HasIndex(x => x.CreatedUtc);

        // Модель не удаляется, а выключается (16.2)
        builder
            .HasOne(x => x.Model)
            .WithMany()
            .HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
