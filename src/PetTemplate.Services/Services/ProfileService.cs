using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Services.Services;

/// <summary>
/// Свой профиль: галки и аватар. Логин, контакты и комментарий — блок «Основное»
/// (<see cref="AccountService"/>, спец. accounts, 6): у них свои отказы «занят»,
/// и сохранять их вторым путём значило бы держать эти отказы в двух местах
/// </summary>
public class ProfileService(ApplicationContext db)
{
    /// <summary>
    /// Сохранить профиль и вернуть его таким, каким он лёг в базу — тем же, что отдаёт
    /// <c>Profile/Details</c>: второй запрос за ним сразу после сохранения не нужен.
    ///
    /// Логин здесь не пишется (спец. accounts, 4.3): у него свой вызов
    /// <c>Account/ChangeUserName</c> и свой отказ «занят». Раньше занятый логин здесь
    /// молча подменялся логином с хвостом из GUID
    /// </summary>
    public async Task<ProfileDto> Save(ProfileDto dto)
    {
        var entity = await db.Profiles
            .Include(x => x.User)
            .SingleAsync(x => x.Id == dto.Id);

        entity.IsAdult = dto.IsAdult;
        entity.HasAcceptedTerms = dto.HasAcceptedTerms;

        await db.SaveChangesAsync();

        return entity.ToDto();
    }

    public async Task SetAvatar(Guid userId, int avatarId)
    {
        var entity = await db.Profiles
            .SingleAsync(x => x.Id == userId);

        entity.AvatarId = avatarId;
        
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Свой профиль — с <c>User</c>: без него <see cref="ProfileMapping"/> не узнает,
    /// есть ли пароль. Наружу всё равно едет <see cref="ProfileDto"/>, а не сущность
    /// </summary>
    public async Task<Profile> Get(Guid userId, CancellationToken ct)
    {
        return await db.Profiles.Include(x => x.User).SingleAsync(x => x.Id == userId, ct);
    }
}
