using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Выдача приглашения (спец. authorization, 6.1)
/// </summary>
public class InvitationCreateDto : IValidatableObject
{
    /// <summary>
    /// Пространство, куда зовут. В теле оно нужно ради сверки с заголовком
    /// <c>X-Workspace-Id</c>, которую делает <c>AuthorizePermissions</c>
    /// </summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// Имя приглашаемого — необязательное, попадёт в текст на лендинге
    /// </summary>
    [TrimmedStringLength(InvitationConstants.InviteeNameMaxLength)]
    public string? InviteeName { get; set; }

    /// <summary>
    /// Права, которые получит принявший
    /// </summary>
    public List<PermissionEnum> Permissions { get; set; } = [];

    /// <summary>
    /// Приглашение без прав не даёт даже войти в пространство. Видно из самого
    /// запроса — <c>422</c> у поля, а не <c>400</c> из сервиса
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Permissions.Any(x => x != PermissionEnum.None))
        {
            yield return new ValidationResult(InvitationMessages.EmptyPermissions.Code, [nameof(Permissions)]);
        }
    }
}
