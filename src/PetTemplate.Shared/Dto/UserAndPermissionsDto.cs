using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Правка прав участника (<c>Users/SetPermissions</c>, спец. authorization, 5).
/// <see cref="WorkspaceId"/> в теле — ради сверки с заголовком <c>X-Workspace-Id</c>,
/// которую делает <c>AuthorizePermissions</c>
/// </summary>
public class UserAndPermissionsDto : IValidatableObject
{
    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public IEnumerable<PermissionEnum> Permissions { get; set; } = [];

    /// <summary>
    /// Пустой набор — это уже не участник; убирают участника отдельным действием
    /// (<c>Workspace/RemoveMember</c>). Видно из самого запроса, поэтому <c>422</c>
    /// у поля, а не <c>400</c> из сервиса
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Permissions.Any(x => x != PermissionEnum.None))
        {
            yield return new ValidationResult(WorkspaceMessages.EmptyPermissions.Code, [nameof(Permissions)]);
        }
    }
}
