using PetTemplate.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Api.Controllers;

[Route("api/[controller]/[action]")]
[Authorize]
[ValidateRequest]
public class ProfileController(ProfileService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] ProfileDto profile)
    {
        profile.Id = User.GetUserId();
        await service.Save(profile);
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> SetAvatar([FromBody] SetAvatarDto dto)
    {
        var userId = User.GetUserId();
        await service.SetAvatar(userId, dto.AvatarId);
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> Details(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var res = await service.Get(userId, ct);
        return Ok(res);
    }
}