using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Админы и их роли (спец. admin, 3.1 и 3.4): список, выдача и снятие роли,
/// удаление админа. Заводятся админы не здесь, а приглашением — выдать роль можно
/// только тому, у кого роли уже есть
/// </summary>
[Route("api/Admin/Admins")]
[OutsideWorkspace]
[AuthorizeAdmin(AdminRole.AdminManagement)]
public class AdminAdminsController(AdminMemberService members) : ControllerBase
{
    [HttpGet]
    [Produces(typeof(AdminMemberDto[]))]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await members.List(ct));

    [HttpPost("{userId:guid}/Roles/{role}")]
    public async Task<IActionResult> Grant(Guid userId, AdminRole role, CancellationToken ct) =>
        await members.Grant(userId, role, User.GetUserId(), ct) ? Ok() : NotFound();

    /// <summary>Последнюю роль так не снять — только удалением админа</summary>
    [HttpDelete("{userId:guid}/Roles/{role}")]
    public async Task<IActionResult> Revoke(Guid userId, AdminRole role, CancellationToken ct) =>
        await members.Revoke(userId, role, User.GetUserId(), ct) ? Ok() : NotFound();

    /// <summary>Все роли разом: человек уходит из админки, аккаунт в продукте остаётся</summary>
    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken ct) =>
        await members.Remove(userId, User.GetUserId(), ct) ? Ok() : NotFound();
}
