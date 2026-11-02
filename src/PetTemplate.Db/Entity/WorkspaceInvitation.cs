using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Приглашение в пространство по ссылке (спец. authorization, 6).
/// Здесь живёт весь жизненный цикл приглашения — выдача, срок, отзыв, расход;
/// членство в пространстве приглашение не хранит: принятое дописывает строки
/// в <see cref="UserInPermission"/>, ровно как это делает управление правами.
/// </summary>
public class WorkspaceInvitation
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Пространство, куда зовут
    /// </summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// Кто позвал
    /// </summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>
    /// Сама ссылка: <c>Guid.NewGuid()</c>, то есть <b>v4</b>, а не v7, как остальные
    /// идентификаторы проекта. У v7 случайных бит 74 и есть метка времени — у выданных
    /// подряд приглашений они предсказуемо рядом; у v4 их 122, и подбор безнадёжен.
    /// Отдельной колонкой, а не поверх <see cref="Id"/>: <c>Id</c> ездит в списке
    /// выданных и в отзыве, и пропуском внутрь быть не должен
    /// </summary>
    public Guid Token { get; set; }

    /// <summary>
    /// Имя приглашаемого — необязательное, подставляется в текст на лендинге
    /// («Привет, Валера!»). Экранирует его клиент, в базу пишем как есть
    /// </summary>
    [MaxLength(InvitationConstants.InviteeNameMaxLength)]
    public string? InviteeName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// <c>CreatedAtUtc</c> + 3 суток (<see cref="InvitationConstants.Lifetime"/>)
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Заполнено → ссылка израсходована: приглашение одноразовое
    /// </summary>
    public DateTime? AcceptedAtUtc { get; set; }

    /// <summary>
    /// Кто пришёл по ссылке — чтобы в списке выданных показать ник принявшего
    /// </summary>
    public Guid? AcceptedBy { get; set; }

    /// <summary>
    /// Отзыв до того, как по ссылке пришли (спец. authorization, 6.2)
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    public virtual Workspace Workspace { get; set; } = null!;

    public virtual Profile? CreatedByProfile { get; set; }

    public virtual Profile? AcceptedByProfile { get; set; }

    /// <summary>
    /// Права, которые приглашение выдаёт принявшему
    /// </summary>
    public virtual ICollection<WorkspaceInvitationPermission> Permissions { get; set; } =
        new List<WorkspaceInvitationPermission>();
}

/// <summary>
/// Право, которое выдаёт приглашение. Отдельной таблицей, а не битовой маской:
/// <see cref="PermissionEnum"/> — не <c>[Flags]</c>, и склейка значений в число завела бы
/// второй, невидимый контракт нумерации
/// </summary>
public class WorkspaceInvitationPermission
{
    public Guid InvitationId { get; set; }

    public PermissionEnum PermissionId { get; set; }

    public virtual WorkspaceInvitation Invitation { get; set; } = null!;
}

public class WorkspaceInvitationConfiguration : IEntityTypeConfiguration<WorkspaceInvitation>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvitation> builder)
    {
        // По токену ходит лендинг — это единственный способ найти строку снаружи
        builder.HasIndex(x => x.Token).IsUnique();

        // Список выданных тянется по пространству, фоновая чистка — по дате выдачи
        builder.HasIndex(x => x.WorkspaceId);
        builder.HasIndex(x => x.CreatedAtUtc);

        // Одноразовость ссылки держит база, а не только проверка в коде: с токеном
        // конкуренции расход уезжает как `UPDATE … WHERE accepted_by IS NULL`,
        // и второй из двух одновременных запросов не меняет ни одной строки —
        // EF отвечает `DbUpdateConcurrencyException`, а сервис переводит его
        // в отказ «по ссылке уже пришли». Колонку под это не заводим (и `xmin`
        // не берём): сгодилась сама отметка о расходе — другие правки строки,
        // отзыв, расходу не мешают
        builder.Property(x => x.AcceptedBy).IsConcurrencyToken();

        // По пространству каскада нет: приглашения сносит чистка пространства своим порядком
        // (WorkspaceService.PurgeContents). Пригласивший и принявший — авторство: удалён
        // аккаунт — ссылка пустеет
        builder
            .HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(x => x.CreatedByProfile)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(x => x.AcceptedByProfile)
            .WithMany()
            .HasForeignKey(x => x.AcceptedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class WorkspaceInvitationPermissionConfiguration : IEntityTypeConfiguration<WorkspaceInvitationPermission>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvitationPermission> builder)
    {
        builder.HasKey(x => new { x.InvitationId, x.PermissionId });

        // Каскад здесь — внутренняя связь одной сущности: набор прав без своего
        // приглашения не значит ничего. К политике удаления пользователей
        // и пространств отношения не имеет
        builder
            .HasOne(x => x.Invitation)
            .WithMany(x => x.Permissions)
            .HasForeignKey(x => x.InvitationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
