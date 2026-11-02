using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Приглашения в пространство по ссылке (спец. authorization, 6).
/// Лендинг по ссылке анонимный, поэтому <c>[AllowAnonymous]</c> стоит точечно.
/// У анонимных методов и у <see cref="Accept"/> нет <c>workspaceId</c> в параметрах и нет
/// <c>AuthorizePermissions</c>: пространство берётся из приглашения, а заголовок
/// <c>X-Workspace-Id</c> клиент может прислать по инерции (интерцептор ставит его
/// на всё), и требовать его здесь нельзя
/// </summary>
[Route("api/[controller]/[action]")]
[Authorize]
[ValidateRequest]
public class InvitationController(WorkspaceInvitationService service, AuthResultFactory authResult) : ControllerBase
{
    [HttpPost]
    [Produces(typeof(WorkspaceInvitationDto))]
    [AuthorizePermissions(PermissionEnum.WorkspaceMemberCrud)]
    public async Task<IActionResult> Create([FromBody] InvitationCreateDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var res = await service.Create(dto, userId, ct);
        return Ok(res);
    }

    [HttpGet]
    [Produces(typeof(List<WorkspaceInvitationDto>))]
    [AuthorizePermissions(PermissionEnum.WorkspaceMemberCrud, PermissionEnum.WorkspaceMemberRead)]
    public async Task<IActionResult> GetList([FromQuery] Guid workspaceId, CancellationToken ct)
    {
        var res = await service.GetList(workspaceId, ct);
        return Ok(res);
    }

    [HttpPost]
    [AuthorizePermissions(PermissionEnum.WorkspaceMemberCrud)]
    public async Task<IActionResult> Revoke([FromQuery] Guid id, [FromQuery] Guid workspaceId, CancellationToken ct)
    {
        await service.Revoke(id, workspaceId, ct);
        return NoContent();
    }

    /// <summary>
    /// Что показать перешедшему по ссылке — до всякой авторизации
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Produces(typeof(InvitationPreviewDto))]
    public async Task<IActionResult> Preview([FromQuery] Guid token, CancellationToken ct)
    {
        var res = await service.Preview(token, ct);
        return Ok(res);
    }

    /// <summary>
    /// Регистрация по ссылке (сценарий Б): заводим профиль и сразу пускаем внутрь.
    /// Регистрация паролем — свежий вход (спец. accounts, 8.6), как у <c>Auth/Register</c>
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> Register([FromBody] InvitationRegisterDto dto, CancellationToken ct)
    {
        var user = await service.RegisterAndAccept(dto, ct);
        return Ok(authResult.Build(user, SessionLogin.Password()));
    }

    /// <summary>
    /// Принятие ссылки уже вошедшим (сценарий А). Прав в пространстве у него пока нет,
    /// поэтому здесь только <c>[Authorize]</c>; куда зовут — знает сам токен.
    /// В ответе новая сессия: без неё свежие права не доедут до клиента
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> Accept([FromBody] InvitationAcceptDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await service.Accept(dto.Token, userId, ct);
        return Ok(authResult.Build(user));
    }
}
