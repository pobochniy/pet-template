using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Шаг трассы прогона (спец. assistant, 13.1): что ушло в модель, какой инструмент
/// позвали, что вернулось, сколько это заняло.
///
/// Трасса — запись для людей, и только: в контекст модели она не едет, упавший ход
/// по ней не возобновляется, ожидание ответа от неё не зависит. Ничего продуктового
/// на ней не висит, поэтому её можно усекать и чистить свободно — живёт она
/// <c>TraceRetentionDays</c> дней, чистит её общий суточный <c>DailyCleanupWorker</c>.
/// </summary>
public class AssistantTraceStep
{
    /// <summary>
    /// Автоинкремент, а не <c>Guid</c>: строк у одного хода до трёх десятков,
    /// идентификатор никому не нужен до вставки, а сама таблица — самая частая
    /// на запись и самая недолговечная
    /// </summary>
    [Key]
    public long Id { get; set; }

    public Guid RunId { get; set; }

    /// <summary>
    /// Номер шага хода — тот же, что в <see cref="AssistantRun.Step"/>
    /// </summary>
    public int Step { get; set; }

    /// <summary>
    /// <c>model</c> или <c>tool</c>. Строка, а не перечисление: трасса ничего
    /// не решает в продукте, и разбирает её только экран разбора полётов
    /// </summary>
    [MaxLength(16)]
    public required string Kind { get; set; }

    /// <summary>
    /// Имя инструмента — пусто у шага обращения к модели
    /// </summary>
    [MaxLength(64)]
    public string? ToolName { get; set; }

    /// <summary>
    /// Что ушло в модель или аргументы инструмента. Усечение до 4 КБ делает писатель,
    /// а не сущность: больше человек глазами не прочтёт, а другого потребителя
    /// у трассы нет
    /// </summary>
    public required string Input { get; set; }

    /// <summary>
    /// Что вернулось — усечено там же и так же
    /// </summary>
    public string? Output { get; set; }

    public int DurationMs { get; set; }

    public int TokensIn { get; set; }

    public int TokensOut { get; set; }

    /// <summary>
    /// Ошибка шага, если он не удался
    /// </summary>
    public string? Error { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Навигационное свойство — прогон
    /// </summary>
    public virtual AssistantRun Run { get; set; } = null!;
}

public class AssistantTraceStepConfiguration : IEntityTypeConfiguration<AssistantTraceStep>
{
    public void Configure(EntityTypeBuilder<AssistantTraceStep> builder)
    {
        // «Показать ход мысли» читает шаги одного прогона по порядку (13.2)
        builder.HasIndex(x => new { x.RunId, x.Step });

        // Чистка ходит по возрасту, а не по прогону (13.1)
        builder.HasIndex(x => x.CreatedAtUtc);

        // Трасса без своего прогона не значит ничего: уходит каскадом и с ним —
        // при удалении аккаунта и при сносе пространства
        builder
            .HasOne(x => x.Run)
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
