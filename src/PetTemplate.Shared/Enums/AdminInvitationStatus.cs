namespace PetTemplate.Shared.Enums;

/// <summary>
/// Статус приглашения админа (спец. admin, 3.4). Номера уезжают в базу —
/// добавлять только в конец.
///
/// <see cref="Expired"/> в базу не пишется: просрочка — сравнение срока с текущим
/// временем, а не событие, и джобы, которая переводила бы строки, нет. В колонке
/// у просроченной остаётся <see cref="Pending"/>, наружу она уезжает уже <see cref="Expired"/>.
///
/// Продублировано в <c>projects/admin/src/app/core/admin-invitation-status.ts</c>
/// </summary>
public enum AdminInvitationStatus
{
    Pending = 0,

    Accepted = 1,

    Revoked = 2,

    /// <summary>Только в ответах: в базе у просроченной — <see cref="Pending"/></summary>
    Expired = 3
}
