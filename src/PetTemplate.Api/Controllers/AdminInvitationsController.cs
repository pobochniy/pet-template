using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Выданные приглашения админов (спец. admin, 3.4): список со статусами, выдача
/// с набором ролей и отзыв непринятого. Страница приглашения и его принятие —
/// в <see cref="AdminAuthController"/>: ролей у того, кто по ссылке пришёл, ещё нет
/// </summary>
[Route("api/Admin/Invitations")]
[OutsideWorkspace]
[AuthorizeAdmin(AdminRole.AdminManagement)]
public class AdminInvitationsController(AdminInvitationService invitations) : ControllerBase
{
    [HttpGet]
    [Produces(typeof(AdminInvitationDto[]))]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await invitations.List(ct));

    [HttpPost]
    [Produces(typeof(AdminInvitationIssuedDto))]
    public async Task<IActionResult> Create([FromBody] AdminInvitationCreateDto dto, CancellationToken ct) =>
        Ok(await invitations.Create(dto, User.GetUserId(), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) =>
        await invitations.Revoke(id, User.GetUserId(), ct) ? Ok() : NotFound();
}
