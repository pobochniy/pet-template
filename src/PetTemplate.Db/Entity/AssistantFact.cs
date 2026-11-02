using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Факт памяти о человеке или о пространстве (спец. assistant, 12.5).
///
/// Храним только то, чего нет в таблицах: псевдонимы, регулярные обстоятельства
/// и умолчания. Манеры, характера и психологического портрета здесь не будет —
/// правило проверки кандидата простое: храним то, что человек согласился бы
/// увидеть написанным про себя.
///
/// «Очистить» в чате факты не трогает: тред — переписка, память — то немногое,
/// что из неё вынесли и оставили насовсем.
/// </summary>
public class AssistantFact
{
    /// <summary>
    /// Уникальный индекс дедупликации: по нему <c>DbErrors.IsDuplicate</c> узнаёт,
    /// что тот же факт одновременно записал соседний ход
    /// </summary>
    public const string OwnerKeyIndex = "ix_assistant_facts_workspace_id_user_id_kind_key";

    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Факт всегда привязан к пространству: в другом пространстве «домик» значит другое.
    /// Тред шаблона — пара «человек + пространство» (12.1), и память живёт там же
    /// </summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// <c>null</c> — общий факт пространства, его видят все участники; иначе личное
    /// умолчание конкретного человека в этом пространстве
    /// </summary>
    public Guid? UserId { get; set; }

    public AssistantFactKind Kind { get; set; }

    /// <summary>
    /// Короткая нормализованная форма того, <b>про что</b> факт: <c>илья</c>,
    /// <c>домик</c>, <c>встречи</c>. Нижний регистр, без кавычек и знаков препинания.
    /// На нём держатся дедупликация и счёт использования
    /// </summary>
    [MaxLength(64)]
    public required string Key { get; set; }

    /// <summary>
    /// Одна фраза ровно в том виде, в каком её увидит человек
    /// </summary>
    [MaxLength(200)]
    public required string Text { get; set; }

    /// <summary>
    /// Откуда взялся — чтобы из списка фактов дойти до разговора и трассы (13.1).
    /// Внешнего ключа нет: прогоны чистит своё время, а факт переживает их
    /// </summary>
    public Guid? SourceRunId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Когда факт последний раз пригодился — см. <see cref="UseCount"/>
    /// </summary>
    public DateTime? LastUsedAtUtc { get; set; }

    /// <summary>
    /// Сколько раз факт пригодился, а не сколько раз уехал в промпт: уезжают все
    /// тридцать, и такой счётчик мерил бы возраст факта. После хода ключи фактов
    /// прогоняются по тексту реплики человека и ответа ассистента, и совпавшим
    /// достаётся плюс один. На этом счётчике держится вытеснение по лимиту
    /// </summary>
    public int UseCount { get; set; }

    /// <summary>
    /// Навигационное свойство — пространство
    /// </summary>
    public virtual Workspace Workspace { get; set; } = null!;
}

public class AssistantFactConfiguration : IEntityTypeConfiguration<AssistantFact>
{
    public void Configure(EntityTypeBuilder<AssistantFact> builder)
    {
        // Личное уходит вместе с аккаунтом (спец. accounts, раздел 10); слияние
        // успевает перенести его остающемуся до удаления (AccountMergeService)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Дедупликация идёт по четвёрке «пространство, владелец, вид, ключ» (12.5).
        // NULLS NOT DISTINCT: у общего факта UserId пуст, и без него Postgres
        // пропустил бы две общие строки с одним ключом. Основная проверка всё равно
        // в коде (MemoryService.Remember), индекс ловит гонку двух ходов
        builder
            .HasIndex(x => new { x.WorkspaceId, x.UserId, x.Kind, x.Key })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName(AssistantFact.OwnerKeyIndex);

        // Содержимое пространства: каскада от него нет, факты сносит
        // WorkspaceService.PurgeContents до самого пространства (спец. authorization, 8)
        builder
            .HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
