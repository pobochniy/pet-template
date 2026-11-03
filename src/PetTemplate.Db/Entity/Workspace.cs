using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Constants;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Пространство (спец. authorization, 4) — граница доступа: всё содержимое проекта
/// живёт внутри пространства и видно только его участникам.
///
/// Отдельной сущности членства нет и не заводится: участник — тот, у кого есть
/// строки <see cref="UserInPermission"/> с этим пространством.
///
/// Точка расширения: раздел проекта, заводящий содержимое пространства, вешает на него
/// внешний ключ с <c>Restrict</c> (каскада от пространства нет), добавляет свои даты
/// в расчёт активности и своё удаление — в порядок сноса (<c>WorkspaceService.PurgeContents</c>)
/// </summary>
public class Workspace
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(WorkspaceConstants.NameMaxLength)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// С этой даты считается активность пустого пространства: удалять его назавтра
    /// после «создал и не успел позвать» было бы сюрпризом (спец. authorization, 8)
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public ICollection<UserInPermission> UserPermissions { get; set; } = new List<UserInPermission>();
}
