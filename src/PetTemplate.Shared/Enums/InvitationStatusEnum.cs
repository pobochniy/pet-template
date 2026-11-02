namespace PetTemplate.Shared.Enums;

/// <summary>
/// Состояние приглашения в пространство (спец. authorization, 6.2).
/// Отдельной колонки в базе нет: статус считается по трём полям строки
/// (<c>ExpiresAtUtc</c>, <c>AcceptedAtUtc</c>, <c>RevokedAtUtc</c>), потому что
/// просрочка — это сравнение с текущим временем, а не запись.
///
/// должно быть продублировано в
/// web/src/app/shared/enums/invitation-status.enum.ts
/// </summary>
public enum InvitationStatusEnum
{
    /// <summary>
    /// Ссылка живая: срок не вышел, не отозвана и по ней ещё не пришли
    /// </summary>
    Active = 0,

    /// <summary>
    /// По ссылке уже пришли — приглашение одноразовое, второй раз не сработает
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Срок вышел. Этим же статусом отвечаем на несуществующий токен: разные ответы
    /// превратили бы эндпоинт в подтверждатель существования токенов
    /// </summary>
    Expired = 2,

    /// <summary>
    /// Отозвано до того, как по ссылке пришли
    /// </summary>
    Revoked = 3
}
