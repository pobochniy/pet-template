using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Автооценка диалога (спец. admin, 16.3): что модель-судья сказала о прогоне.
/// Подсказка, чтобы из сотни диалогов открыть глазами десять худших, а не приговор —
/// автоматических действий на ней не висит.
///
/// Переписки здесь нет — только цифры и одна-две фразы судьи в <see cref="Notes"/>, —
/// поэтому оценка живёт <c>EvaluationRetentionDays</c> (год), дольше трассы: по ней
/// видно, стало ли за полгода лучше.
///
/// Внешнего ключа на прогон нет нарочно: прогон уходит со сносом пространства,
/// а оценка — цифры о работе ассистента, а не содержимое пространства. Деньги судьи —
/// строкой <see cref="ServiceSpend"/>; <see cref="Cost"/> здесь — цена рядом с метриками
/// </summary>
public class DialogEvaluation
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Одна оценка на прогон — уникальный индекс</summary>
    public Guid RunId { get; set; }

    /// <summary>Кто судил — строка справочника моделей</summary>
    public Guid ModelId { get; set; }

    /// <summary>Получил ли человек то, что просил; пусто — судья не понял</summary>
    public bool? Succeeded { get; set; }

    /// <summary>Доволен ли ответом, 1–5; пусто — судья не понял</summary>
    public int? Satisfaction { get; set; }

    /// <summary>
    /// Сколько уточнений понадобилось. Пусто, только когда судья ответил не по
    /// формату: строка всё равно пишется, потому что оценка уже оплачена
    /// </summary>
    public int? Clarifications { get; set; }

    /// <summary>Сколько раз человек пересобирал план («да, но»); пусто — как у уточнений</summary>
    public int? Replans { get; set; }

    /// <summary>Ошибся ли агент: не тот инструмент, не та дата, потерял контекст</summary>
    public bool? AgentMistake { get; set; }

    /// <summary>Куда упёрся; пусто — ни во что</summary>
    public DialogStopReason? StopReason { get; set; }

    /// <summary>Одна-две фразы судьи — для сортировки и глаз, без цитат из переписки</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>Себестоимость самой оценки по цене судьи, в валюте прайса</summary>
    [Precision(18, 6)]
    public decimal Cost { get; set; }

    /// <summary>От него считают чистка и сводка за период</summary>
    public DateTime CreatedUtc { get; set; }

    public virtual AiModel Model { get; set; } = null!;
}

public class DialogEvaluationConfiguration : IEntityTypeConfiguration<DialogEvaluation>
{
    /// <summary>
    /// Имя уникального индекса «одна оценка на прогон» — по нему <c>DbErrors.IsDuplicate</c>
    /// узнаёт повтор команды, который успел раньше
    /// </summary>
    public const string RunIndex = "ix_dialog_evaluations_run_id";

    public void Configure(EntityTypeBuilder<DialogEvaluation> builder)
    {
        // Одна оценка на прогон: повтор команды ловит база, а не проверка перед вставкой
        builder.HasIndex(x => x.RunId).IsUnique().HasDatabaseName(RunIndex);

        // Суточная чистка
        builder.HasIndex(x => x.CreatedUtc);

        // Модель не удаляется, а выключается (спец. assistant, 16.2)
        builder
            .HasOne(x => x.Model)
            .WithMany()
            .HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
