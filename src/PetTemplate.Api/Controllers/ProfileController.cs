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
    /// <summary>
    /// Сохранить профиль и ответить сохранённым — тем же, что отдаёт
    /// <see cref="Details"/>. Логин и контакты здесь не пишутся: у них блок «Основное»
    /// (<c>AccountController</c>, спец. accounts, 6)
    /// </summary>
    [HttpPost]
    [Produces(typeof(ProfileDto))]
    public async Task<IActionResult> Save([FromBody] ProfileDto profile)
    {
        profile.Id = User.GetUserId();
        return Ok(await service.Save(profile));
    }

    [HttpPost]
    public async Task<IActionResult> SetAvatar([FromBody] SetAvatarDto dto)
    {
        var userId = User.GetUserId();
        await service.SetAvatar(userId, dto.AvatarId);
        return Ok();
    }

    /// <summary>
    /// Свой профиль целиком. Наружу едет <see cref="ProfileDto"/>, а не сущность:
    /// почему — в <see cref="ProfileMapping"/>
    /// </summary>
    [HttpGet]
    [Produces(typeof(ProfileDto))]
    public async Task<IActionResult> Details(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var res = await service.Get(userId, ct);
        return Ok(res.ToDto());
    }
}