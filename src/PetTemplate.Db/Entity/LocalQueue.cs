using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Строка очереди команд (спец. mail, 4; спец. infrastructure, 2.2). Команда ложится сюда
/// тем же <c>SaveChanges</c>, что и доменное изменение (outbox,
/// <c>CommandQueue.Enqueue</c>), а разбирают её воркеры пакета <c>AsuTrack.LocalQueue</c>.
///
/// Колонки — ровно те, что ждёт хранилище <c>AsuTrack.LocalQueue.PostgreSql</c>: тип
/// команды — <c>FullName</c>, тело — JSON по умолчанию. Id — родной <c>uuid</c> Postgres
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
