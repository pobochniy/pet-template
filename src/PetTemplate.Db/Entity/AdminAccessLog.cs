using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Кто читал чужую переписку (спец. admin, 16.2): строка на каждое открытие диалога
/// в админке. Это единственное, что отличает «инструмент диагностики» от «мы читаем
/// вашу переписку»: запись пишется всегда, до ответа, и удалить её нечем, кроме срока
/// хранения.
///
/// Содержимого здесь нет — только идентификаторы, — и живёт строка
/// <c>AccessLogRetentionDays</c> (год), дольше самой трассы: на вопрос «кто читал мою
/// переписку в марте» ответ должен найтись и в мае.
///
/// Ключей на прогон и пространство нет нарочно: прогон уходит вместе с пространством,
/// а запись о чтении должна их пережить
/// </summary>
public class AdminAccessLog
{
    [Key]
    public Guid Id { get; set; }

    public Guid? AdminUserId { get; set; }

    /// <summary>Какой прогон открыли</summary>
    public Guid RunId { get; set; }

    /// <summary>Чей это прогон — пишется сразу: после сноса пространства спросить будет не у кого</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>От него считает чистка и фильтр по периоду</summary>
    public DateTime CreatedUtc { get; set; }
}

public class AdminAccessLogConfiguration : IEntityTypeConfiguration<AdminAccessLog>
{
    public void Configure(EntityTypeBuilder<AdminAccessLog> builder)
    {
        // Таблица — журнал, а не набор журналов: имя в единственном числе, как у очереди
        builder.ToTable("admin_access_log");

        // Журнал админки переживает админа — ссылка пустеет (спец. accounts, раздел 10)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AdminUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Под суточную чистку и список за период
        builder.HasIndex(x => x.CreatedUtc);

        // Фильтр вкладки «Журнал доступа» по админу
        builder.HasIndex(x => new { x.AdminUserId, x.CreatedUtc });
    }
}
