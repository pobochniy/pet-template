using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Прогон — один ход ассистента целиком (спец. assistant, 12.3). Одношаговый
/// и тридцатишаговый записываются одинаково: короткому запись нужна ради метрик
/// и трассы, длинному — ещё и чтобы пережить перезапуск API.
///
/// Срока жизни у прогона нет: строка короткая и без куска переписки и уходит лишь
/// вместе с пространством. На ней держатся идемпотентность по <c>RunId</c>, метрики
/// учёт расхода (13.5) и разбор диалогов в админке (спец. admin, 16).
/// </summary>
public class AssistantRun
{
    /// <summary>
    /// <c>RunId</c>, он же ключ идемпотентности команды хода (раздел 3)
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// Часовой пояс, зафиксированный в момент просьбы (раздел 10): «завтра» считается
    /// от него, а не от зоны сервера, и переезд человека прошлый ход не переписывает
    /// </summary>
    [MaxLength(64)]
    public required string TimeZone { get; set; }

    /// <summary>
    /// Язык интерфейса автора в момент просьбы — уезжает в системный промпт
    /// требованием к языку ответа (раздел 17)
    /// </summary>
    [MaxLength(8)]
    public required string Language { get; set; }

    public AssistantRunStatus Status { get; set; }

    /// <summary>
    /// Последний завершённый шаг — для плашки в ленте и метрик. Упавший ход с него
    /// не возобновляется, а начинается заново (раздел 3)
    /// </summary>
    public int Step { get; set; }

    /// <summary>
    /// Перешагнул ли ход малый бюджет <c>StepsSoft</c> (раздел 3). Режима, который
    /// можно было бы записать вместо этого признака, у хода нет
    /// </summary>
    public bool Escalated { get; set; }

    public AssistantStopReason? StopReason { get; set; }

    /// <summary>
    /// Входящие токены хода, как их считает поставщик. Копятся по всем попыткам хода:
    /// упавшая попытка тоже израсходовала токены у поставщика. При завершении хода
    /// они уезжают в строку расхода <see cref="RunSpend"/> (13.5)
    /// </summary>
    public long TokensIn { get; set; }

    /// <summary>Из входящих — прочитанные поставщиком из кеша</summary>
    public long TokensCached { get; set; }

    public long TokensOut { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>
    /// Чем ход занят прямо сейчас — имя инструмента для плашки в ленте (раздел 3).
    /// Имя, а не текст для человека: подпись собирает клиент своим словарём
    /// </summary>
    [MaxLength(64)]
    public string? CurrentActivity { get; set; }

    /// <summary>
    /// Сколько раз очередь брала команду хода: после второй попытки ход уходит
    /// в <see cref="AssistantRunStatus.Failed"/> (раздел 3). Считаем здесь, а не
    /// в очереди, потому что человеку виден именно ход
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Тред стёрли «Очистить», пока ход ещё шёл (спец. assistant, 12.1.1): ход заканчивается
    /// как обычно — статус, расход, — но ни сообщения, ни плана в пустую ленту не пишет,
    /// а трассу, дописанную после очистки, уносит
    /// </summary>
    public bool Cleared { get; set; }

    /// <summary>
    /// Какой моделью справочника шёл ход. Пишется на старте попытки, до первого
    /// обращения к модели: разбору диалогов модель нужна и у хода с нулевыми
    /// токенами. Пусто у ещё не начатых
    /// </summary>
    public Guid? AiModelId { get; set; }

    public virtual AiModel? AiModel { get; set; }

    /// <summary>
    /// Навигационное свойство — пространство
    /// </summary>
    public virtual Workspace Workspace { get; set; } = null!;
}

public class AssistantRunConfiguration : IEntityTypeConfiguration<AssistantRun>
{
    public void Configure(EntityTypeBuilder<AssistantRun> builder)
    {
        // Личное уходит вместе с аккаунтом (спец. accounts, раздел 10)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // «Идёт ли у этой пары ход» спрашивает и лента, и постановка следующего
        // сообщения в очередь треда (раздел 3)
        builder.HasIndex(x => new { x.UserId, x.WorkspaceId, x.Status });

        // Ходов у треда одновременно не больше одного (раздел 3) — и держит это база,
        // а не проверка «есть ли живой» перед вставкой: два «Отправить» с двух вкладок
        // проходят проверку оба. Проигравший получает 23505 и встаёт в очередь треда.
        // Живой — не законченный, а не Running: остановленный ход ещё дописывает конец,
        // и второй ход рядом с ним писал бы в тот же тред
        builder
            .HasIndex(x => new { x.UserId, x.WorkspaceId })
            .IsUnique()
            .HasFilter("finished_at_utc IS NULL")
            .HasDatabaseName(RunningIndex);

        builder
            .HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Модель, которой ходили, не удаляется, а выключается (спец. admin, 15.1)
        builder
            .HasOne(x => x.AiModel)
            .WithMany()
            .HasForeignKey(x => x.AiModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Имя частичного уникального индекса «один незаконченный ход на тред» — по нему
    /// <c>DbErrors.IsDuplicate</c> узнаёт проигранную гонку двух отправок
    /// </summary>
    public const string RunningIndex = "ix_assistant_runs_running";
}
