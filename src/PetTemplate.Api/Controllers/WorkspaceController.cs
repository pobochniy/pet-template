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
/// Пространства пользователя (спец. authorization, 3, 7 и 8). Большая часть методов —
/// без <see cref="AuthorizePermissions"/>: создать первое, сменить основное и уйти
/// можно, не имея прав в пространстве из заголовка, — а то и не имея пространств вовсе
/// </summary>
[Route("api/[controller]/[action]")]
[Authorize]
[ValidateRequest]
public class WorkspaceController(
    WorkspaceService workspaceService,
    AuthService authService,
    AuthResultFactory authResult) : ControllerBase
{
    /// <summary>
    /// Создаёт пространство и сразу отдаёт новую сессию (спец. authorization, 3, п. 5).
    ///
    /// Токен в ответе — не удобство, а условие работоспособности: полный набор прав
    /// создателя появляется в базе уже после того, как его текущий JWT подписан,
    /// и до перевыпуска сервер считает, что в новом пространстве этого пользователя
    /// нет (403 на закрытые правами методы). Отдельным вызовом это лечилось
    /// в два шага, из которых второй мог не случиться
    /// </summary>
    [HttpPost]
    [Produces(typeof(WorkspaceCreatedDto))]
    public async Task<IActionResult> Create([FromBody] WorkspaceDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var workspaceId = await workspaceService.Create(dto, userId, ct);

        var user = await authService.GetUser(userId, ct);

        return Ok(new WorkspaceCreatedDto
        {
            WorkspaceId = workspaceId,
            Authentication = authResult.Build(user)
        });
    }

    /// <summary>
    /// Правка пространства из заголовка — название. Пространство не обязано быть
    /// активным на клиенте: строка в списке своих шлёт заголовок своего пространства
    /// (спец. authorization, 7.2). <c>Id</c> из тела не читается — правится то,
    /// на которое проверено право
    /// </summary>
    [HttpPost]
    [AuthorizePermissions(PermissionEnum.WorkspaceInfoCrud)]
    public async Task<IActionResult> Update([FromBody] WorkspaceDto dto, CancellationToken ct)
    {
        await workspaceService.Update(HttpContext.GetWorkspaceId(), dto, ct);
        return NoContent();
    }

    /// <summary>
    /// Основное пространство — свойство пользователя, а не право в пространстве,
    /// поэтому здесь только <c>[Authorize]</c>: менять его можно, находясь в любом своём.
    /// Чужое отсечёт сервис
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SetPrimary([FromQuery] Guid workspaceId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await workspaceService.SetPrimary(userId, workspaceId, ct);
        return NoContent();
    }

    /// <summary>
    /// Выход из пространства (спец. authorization, 8). Прав в покидаемом пространстве
    /// не требуем — уйти можно откуда угодно, где состоишь, и не обязательно
    /// из активного: поэтому здесь только <c>[Authorize]</c>, как у <see cref="SetPrimary"/>.
    ///
    /// В ответе новая сессия: права из JWT только что исчезли из базы, и без перевыпуска
    /// клиент продолжал бы считать покинутое пространство своим
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> Leave([FromQuery] Guid workspaceId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await workspaceService.Leave(userId, workspaceId, ct);

        var user = await authService.GetUser(userId, ct);
        return Ok(authResult.Build(user));
    }

    /// <summary>
    /// Убрать участника из пространства — явное действие со страницы участников.
    /// Право то же, что и у выдачи прав: убрать все права и убрать участника —
    /// одно и то же полномочие, разделять их незачем.
    ///
    /// Токен убранного при этом не меняется — общая беда устаревших прав в JWT,
    /// <c>tech-debt/jwt-stale-permissions.md</c>
    /// </summary>
    [HttpPost]
    [AuthorizePermissions(PermissionEnum.PermissionManagement)]
    public async Task<IActionResult> RemoveMember(
        [FromQuery] Guid userId,
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        await workspaceService.RemoveMember(User.GetUserId(), userId, workspaceId, ct);
        return NoContent();
    }

    /// <summary>
    /// Свои пространства — для селектора в шапке и страницы настроек
    /// </summary>
    [HttpGet]
    [Produces(typeof(List<WorkspaceDto>))]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var list = await workspaceService.GetList(userId, ct);
        return Ok(list);
    }
}
