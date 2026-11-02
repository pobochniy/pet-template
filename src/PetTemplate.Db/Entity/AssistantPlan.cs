using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// План ассистента — список операций, показанный человеку до согласия (спец. assistant,
/// 12.2.1).
///
/// Своя сущность, а не колонки сообщения, по двум причинам: план переживает «Очистить»
/// (12.1.1); и у <c>apply_plan</c> есть устойчивый идентификатор вместо «последнее
/// сообщение треда с Pending».
/// </summary>
public class AssistantPlan
{
    /// <summary>
    /// <c>Guid.CreateVersion7()</c>; он же ключ идемпотентности применения (5.1)
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Тред, которому план принадлежит
    /// </summary>
    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// Ход, который план собрал
    /// </summary>
    public Guid? RunId { get; set; }

    public AssistantPlanStatus Status { get; set; }

    /// <summary>
    /// JSON: упорядоченный список операций, у каждой — вид, аргументы и ссылки
    /// <c>#n</c> на результаты предыдущих (<c>PlanDocument</c>). Это то, что применит
    /// <c>apply_plan</c> слово в слово, и то, из чего клиент рисует блок плана.
    /// <c>jsonb</c> без прикладного потолка: длину плана ограничивают двадцать
    /// сущностей (раздел 8), а резать строку незачем
    /// </summary>
    public required string Operations { get; set; }

    /// <summary>
    /// JSON: по операции — идентификатор созданного и отказ с причиной
    /// (<c>PlanResult</c>). Пишется после каждой операции (5.1) и остаётся
    /// единственным источником для отчёта «Готово»
    /// </summary>
    public string? Result { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Момент применения — с ним раздел проекта сверяет «правилась после применения»,
    /// когда у него появится откат своего плана (спец. assistant, 7.1)
    /// </summary>
    public DateTime? AppliedAtUtc { get; set; }

    /// <summary>
    /// Навигационное свойство — пространство
    /// </summary>
    public virtual Workspace Workspace { get; set; } = null!;
}

public class AssistantPlanConfiguration : IEntityTypeConfiguration<AssistantPlan>
{
    public void Configure(EntityTypeBuilder<AssistantPlan> builder)
    {
        // Личное уходит вместе с аккаунтом (спец. accounts, раздел 10)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Незакрытый план в треде один (раздел 8), и ищут его ровно так:
        // «есть ли у этой пары план в Draft или Pending» (12.2.1)
        builder.HasIndex(x => new { x.UserId, x.WorkspaceId, x.Status });

        builder.Property(x => x.Operations).HasColumnType("jsonb");
        builder.Property(x => x.Result).HasColumnType("jsonb");

        // План — содержимое пространства: сносит его WorkspaceService.PurgeContents
        builder
            .HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
