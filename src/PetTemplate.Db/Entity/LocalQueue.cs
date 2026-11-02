using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Строка очереди команд (спец. mail, 4; <c>tech-debt/localqueue-postgres.md</c>). Команда
/// ложится сюда тем же <c>SaveChanges</c>, что и доменное изменение (outbox), а разбирает
/// её <c>CommandQueueWorker</c>.
///
/// Колонки — те же, что у таблицы пакета <c>LocalQueue</c> (Lad, ASU): когда появится
/// <c>LocalQueue.PostgreSql</c>, воркер заменится пакетом, а таблица и постановка
/// (<c>CommandQueue.Enqueue</c>) останутся как есть. Id — родной <c>uuid</c> Postgres,
/// без двоичного представления, которое понадобилось MySQL
/// </summary>
public class LocalQueue
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Команда в JSON — как её сериализует сама команда (<c>ToString()</c>)</summary>
    public string Data { get; set; } = null!;

    /// <summary>Полное имя типа команды — по нему воркер выбирает обработчик</summary>
    [MaxLength(250)]
    public string CommandType { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// До какого времени строку не трогать: её взял воркер или обработка упала и ждёт
    /// повтора. <c>null</c> — свободна
    /// </summary>
    public DateTime? LockedTillUtc { get; set; }

    /// <summary>Сколько раз строку брали в обработку</summary>
    public int TryCount { get; set; }
}

public class LocalQueueConfiguration : IEntityTypeConfiguration<LocalQueue>
{
    public void Configure(EntityTypeBuilder<LocalQueue> builder)
    {
        // Имя таблицы — в единственном числе, как у пакета (`LocalQueue`): ручной SQL
        // воркера обращается к ней по имени
        builder.ToTable("local_queue");

        builder
            .Property(x => x.Data)
            .HasColumnType("jsonb");

        builder
            .Property(x => x.TryCount)
            .HasDefaultValue(0);

        // Выборка воркера — свободные строки своих типов в порядке постановки
        builder.HasIndex(x => new { x.CommandType, x.CreatedAtUtc });
    }
}
