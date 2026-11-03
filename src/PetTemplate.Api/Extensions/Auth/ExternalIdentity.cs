using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Кто вошёл через площадку: id пользователя Telegram или VK и, если площадка его
/// отдала, имя пользователя — кандидат в автоматический логин (спец. accounts, 4.2).
/// Язык — только у параметров запуска mini-app VK: VK называет язык своего интерфейса.
///
/// <paramref name="ConfirmedAt"/> — когда площадка подтвердила вход (спец. accounts, 8.3):
/// <c>iat</c> из <c>id_token</c> Telegram, <c>auth_date</c>/<c>vk_ts</c> подписи mini-app —
/// токен принимается и через минуты после выдачи, подпись mini-app живёт сутки, и окно
/// свежего входа считается от них. Код VK ID одноразовый и только что обменян — там вход
/// случился сейчас, <c>null</c>
/// </summary>
public record ExternalIdentity(long Id, string? UserName, DateTimeOffset? ConfirmedAt = null, string? Language = null)
{
    /// <summary>Вход этой площадкой — для токена со временем и способом входа (<see cref="FreshLogin"/>)</summary>
    public ExternalLogin Via(ExternalProvider provider) => new(provider, ConfirmedAt ?? DateTimeOffset.UtcNow);
}
