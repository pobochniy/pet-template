using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Сообщение треда ассистента (спец. assistant, 12.2). Тред — пара
/// «человек + пространство» (12.1), отдельной сущности под него нет: ключ треда лежит
/// в самих сообщениях.
///
/// Здесь только то, что человек видел на экране. Промежуточные вызовы инструментов
/// сюда не попадают — им место в трассе прогона (13.1), у которой
/// и срок жизни, и доступ другие.
/// </summary>
public class AssistantMessage
{
    /// <summary>
    /// <c>Guid.CreateVersion7()</c> из сервиса: он монотонен, и лента сортируется
    /// по нему же — отдельного порядкового поля не нужно
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Автор треда — вместе с <see cref="WorkspaceId"/> это и есть ключ треда
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Пространство треда: у одного человека в двух пространствах разговоры разные
    /// </summary>
    public Guid WorkspaceId { get; set; }

    public AssistantMessageRole Role { get; set; }

    /// <summary>
    /// Текст сообщения. Колонка <c>text</c> без прикладного потолка: резюме компакции
    /// сворачивает в себя весь предыдущий разговор
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Прогон, породивший сообщение. Пусто у реплики человека, ждущей своей очереди,
    /// и у резюме компакции
    /// </summary>
    public Guid? RunId { get; set; }

    /// <summary>
    /// План, показанный в этом сообщении (12.2.1). По нему клиент рисует блок плана:
    /// модель пишет только прозу вокруг (5.1)
    /// </summary>
    public Guid? PlanId { get; set; }

    /// <summary>
    /// Варианты ответа на уточняющий вопрос, JSON-список строк (5.2). Чипсы под вопросом:
    /// нажатие отправляет в ленту ту самую реплику, которую человек набрал бы руками,
    /// поэтому это строки, а не действия с идентификаторами
    /// </summary>
    public string? Options { get; set; }

    /// <summary>
    /// Факты, записанные этим ходом, — JSON-список идентификаторов (12.5). Строка
    /// «Запомнил: …» живёт вместе с сообщением, а сам факт можно убрать в любой момент,
    /// поэтому здесь ссылки, а текст берётся из памяти на каждое чтение ленты
    /// </summary>
    public string? FactIds { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Навигационное свойство — пользователь
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// Навигационное свойство — пространство
    /// </summary>
    public virtual Workspace Workspace { get; set; } = null!;

    /// <summary>
    /// Навигационное свойство — прогон
    /// </summary>
    public virtual AssistantRun? Run { get; set; }

    /// <summary>
    /// Навигационное свойство — план
    /// </summary>
    public virtual AssistantPlan? Plan { get; set; }
}

public class AssistantMessageConfiguration : IEntityTypeConfiguration<AssistantMessage>
{
    public void Configure(EntityTypeBuilder<AssistantMessage> builder)
    {
        // Лента читается ровно так: тред целиком, по возрастанию Id (12.2)
        builder.HasIndex(x => new { x.UserId, x.WorkspaceId, x.Id });

        builder.Property(x => x.Options).HasColumnType("jsonb");
        builder.Property(x => x.FactIds).HasColumnType("jsonb");

        // Переписка — содержимое пространства: каскада от пространства нет, сносит её
        // WorkspaceService.PurgeContents до самого пространства (спец. authorization, 8)
        builder
            .HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // По UserId — каскад: диалоги ассистента — личное, удаление аккаунта физическое
        // (спец. accounts, раздел 10)
        builder
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Прогон и план живут своей жизнью: план переживает «Очистить» (12.1.1).
        // Сообщение при этом остаётся в ленте — человек видит свою переписку
        // целиком, — поэтому ссылка просто обнуляется
        builder
            .HasOne(x => x.Run)
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(x => x.Plan)
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
