using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Запуск из админки (спец. admin, 15.3 и 16.3): сверка модели набором фраз или
/// автооценка пачки диалогов. И то и другое — долгое, идёт фоном через очередь команд,
/// а экран читает отсюда статус и итог.
///
/// Один запуск одного вида за раз держит база: частичный уникальный индекс по виду
/// у идущих (<c>status = 0</c>).
/// Запуски не чистятся: их единицы в неделю
/// </summary>
public class AdminJob
{
    [Key]
    public Guid Id { get; set; }

    public AdminJobKind Kind { get; set; }

    /// <summary>Выбранная модель: судья у оценки, сверяемая у сверки</summary>
    public Guid ModelId { get; set; }

    /// <summary>Кто запустил. Журнал админки переживает админа — ссылка пустеет</summary>
    public Guid? StartedByUserId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? FinishedUtc { get; set; }

    public AdminJobStatus Status { get; set; }

    /// <summary>Сколько поставлено: прогонов у оценки, фраз у сверки</summary>
    public int Planned { get; set; }

    public int Done { get; set; }

    /// <summary>
    /// У оценки — неоценённые прогоны, которые выбранному судье нельзя (16.3), и прогоны,
    /// пропавшие к своей очереди
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>У оценки — сорвавшиеся на поставщике: повторяет их следующий запуск</summary>
    public int Failed { get; set; }

    /// <summary>Итог по запуску — сумма его строк <see cref="ServiceSpend"/></summary>
    public long TokensIn { get; set; }

    public long TokensCached { get; set; }

    public long TokensOut { get; set; }

    /// <summary>В валюте прайса — сумма служебного расхода запуска</summary>
    [Precision(18, 6)]
    public decimal Cost { get; set; }

    /// <summary>
    /// У сверки — отчёт JSON-документом (доли и строки по фразам, 15.3); пишется после
    /// каждой фразы, поэтому прерванная сверка продолжается с того же места
    /// </summary>
    public string? Report { get; set; }

    /// <summary>Почему запуск кончился <c>Failed</c>, у оценки — последний отказ поставщика</summary>
    [MaxLength(500)]
    public string? Error { get; set; }

    public virtual AiModel Model { get; set; } = null!;
}

public class AdminJobConfiguration : IEntityTypeConfiguration<AdminJob>
{
    /// <summary>
    /// Имя частичного уникального индекса «один запуск одного вида за раз» — по нему
    /// <c>DbErrors.IsDuplicate</c> узнаёт проигранную гонку двух нажатий
    /// </summary>
    public const string RunningIndex = "ix_admin_jobs_running";

    public void Configure(EntityTypeBuilder<AdminJob> builder)
    {
        // Журнал админки переживает админа — ссылка пустеет (спец. accounts, раздел 10)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.StartedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.Report).HasColumnType("jsonb");

        // Один запуск одного вида за раз: второй одновременный упирается в индекс,
        // а не в проверку перед вставкой. 0 — номер Running в перечислении
        builder
            .HasIndex(x => x.Kind)
            .IsUnique()
            .HasFilter("status = 0")
            .HasDatabaseName(RunningIndex);

        // Список запусков на экране — по виду, новые первыми
        builder.HasIndex(x => new { x.Kind, x.CreatedUtc });

        // Модель не удаляется, а выключается (спец. assistant, 16.2)
        builder
            .HasOne(x => x.Model)
            .WithMany()
            .HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
