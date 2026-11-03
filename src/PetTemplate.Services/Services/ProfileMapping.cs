using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Services.Services;

/// <summary>
/// Профиль наружу — всегда <see cref="ProfileDto"/>, никогда сущность.
///
/// У <see cref="Profile"/> есть навигационные свойства <c>User</c>
/// и <c>PrimaryWorkspace</c>, и ответ сущностью держится на том, что их никто
/// не загрузил: стоит появиться <c>Include(x =&gt; x.User)</c> или чтению
/// <c>User</c> в том же scope контекста — и EF подставит их связыванием, а
/// в ответ уедут <c>PasswordHash</c> с <c>SecurityStamp</c>. Клиент кладёт
/// профиль в <c>localStorage</c>, так что уедут они туда надолго.
///
/// Отображение одно на всех, кто отдаёт профиль: <c>Profile/Details</c>,
/// <c>Profile/Save</c> и вход (<see cref="AuthService"/>). Второй копии
/// заводить нельзя — разошлись бы молча, и на разных экранах поле
/// то приезжало бы, то нет
/// </summary>
public static class ProfileMapping
{
    /// <summary>
    /// Профиль как есть — с указателем на основное пространство из самого профиля
    /// </summary>
    public static ProfileDto ToDto(this Profile profile) =>
        profile.ToDto(profile.PrimaryWorkspaceId);

    /// <summary>
    /// То же, но с посчитанным основным пространством: так делает вход
    /// (<see cref="AuthService"/>) — указатель в профиле мог устареть, и <c>null</c>
    /// здесь значит «пространств не осталось», а не «не передали».
    ///
    /// Отдельная перегрузка, а не необязательный параметр: со значением
    /// по умолчанию посчитанный <c>null</c> было бы не отличить от «не передали»,
    /// и вход отдавал бы устаревший указатель на пространство, из которого человек
    /// уже вышел
    /// </summary>
    public static ProfileDto ToDto(this Profile profile, Guid? primaryWorkspaceId) =>
        new()
        {
            Id = profile.Id,
            UserName = profile.UserName,
            UserNameIsGenerated = profile.UserNameIsGenerated,
            // Признак, а не хеш: сам `User` наружу не едет. Вызывающий загружает его
            // (`Include(x => x.User)`) — без него пароль считался бы отсутствующим
            HasPassword = profile.User?.PasswordHash != null,
            AvatarId = profile.AvatarId,
            Email = profile.Email,
            EmailConfirmed = profile.EmailConfirmed,
            Phone = profile.PhoneNumber,
            Comment = profile.Comment,
            Language = profile.Language,
            IsAdult = profile.IsAdult,
            HasAcceptedTerms = profile.HasAcceptedTerms,
            PrimaryWorkspaceId = primaryWorkspaceId
        };
}
