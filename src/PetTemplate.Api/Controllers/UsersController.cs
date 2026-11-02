using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Состав пространства и права участников (спец. authorization, 5)
/// </summary>
[Route("api/[controller]/[action]")]
[Authorize]
[ValidateRequest]
public class UsersController(UsersService service) : ControllerBase
{
    /// <summary>
    /// Участники активного пространства — пространство берём из заголовка.
    /// Своего права у списка нет: знать, с кем ты в одном пространстве, вправе каждый,
    /// кто в нём есть, — поэтому атрибут без прав, то есть «участник». Посторонний — 403.
    /// Без контактов (<see cref="ProfileSummaryDto"/>)
    /// </summary>
    [HttpGet]
    [AuthorizePermissions]
    public async Task<ProfileSummaryDto[]> GetProfiles(CancellationToken ct)
    {
        return await service.GetProfiles(HttpContext.GetWorkspaceId(), ct);
    }

    /// <summary>
    /// Чужие права видит только тот, кто их раздаёт: набор прав — приватная информация,
    /// и симметрично <see cref="SetPermissions"/> требуем <see cref="PermissionEnum.PermissionManagement"/>.
    /// Совпадение <paramref name="workspaceId"/> с заголовком проверяет <see cref="PermissionsValidation"/>
    /// </summary>
    [HttpGet]
    [AuthorizePermissions(PermissionEnum.PermissionManagement)]
    public async Task<PermissionEnum[]> GetPermissions([FromQuery] Guid userId, [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        return await service.GetPermissions(userId, workspaceId, ct);
    }

    /// <summary>
    /// Правка прав участника. Кто правит — берём из токена, а не из тела:
    /// выдать больше, чем есть у самого правящего, нельзя (спец. authorization, 5).
    /// Пустой набор — <c>422</c> у поля <c>Permissions</c> (<see cref="UserAndPermissionsDto"/>)
    /// </summary>
    [HttpPost]
    [AuthorizePermissions(PermissionEnum.PermissionManagement)]
    public async Task<IActionResult> SetPermissions([FromBody] UserAndPermissionsDto dto, CancellationToken ct)
    {
        await service.SetPermissions(dto.UserId, dto.WorkspaceId, dto.Permissions, User.GetUserId(), ct);
        return NoContent();
    }
}
