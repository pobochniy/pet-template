using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Constants;

/// <summary>
/// Отказы по приглашению (спец. authorization, 6.4). Лендинг рисует их сам
/// по статусу из <c>Preview</c>, а мутации отвечают тем же отказом в теле ошибки —
/// поэтому формулировки лежат в одном месте. Тексты выдачи и отзыва клиент
/// не дублирует, но лежат они здесь же: искать формулировку в двух местах хуже,
/// чем держать в одном файле и то, что зеркалирования не требует.
///
/// С клиентом синхронизированы <see cref="Expired"/>, <see cref="Revoked"/>
/// и <see cref="Accepted"/> — их рисует лендинг по статусу:
/// web/src/app/invite/invitation-messages.ts. Перевод — словарь <c>invite</c>
/// </summary>
public static class InvitationMessages
{
    /// <summary>
    /// Ненайденный токен и просроченный отвечают одинаково и намеренно: разные ответы
    /// превратили бы эндпоинт в подтверждатель существования токенов. Разница для
    /// человека нулевая — обе просьбы одна и та же
    /// </summary>
    public static readonly ErrorMessage Expired = new(
        "invite.error.expired",
        "Ссылка недействительна или срок её действия истёк. Попросите новую");

    public static readonly ErrorMessage Revoked = new(
        "invite.error.revoked",
        "Приглашение отозвано. Попросите новую ссылку");

    public static readonly ErrorMessage Accepted = new(
        "invite.error.accepted",
        "По этой ссылке уже зарегистрировались. Если это были вы — войдите " +
        "под своим логином. Если нет — попросите новую ссылку");

    public static readonly ErrorMessage AlreadyMember = new(
        "invite.error.alreadyMember",
        "Вы уже участник этого пространства");

    /// <summary>
    /// Регистрация по ссылке отказывает теми же словами и тем же кодом, что и обычная:
    /// правило одно (спец. accounts, 4.1), и формулировок «занято» не должно быть две
    /// </summary>
    public static readonly ErrorMessage UserNameTaken = AuthMessages.UserNameTaken;

    public static readonly ErrorMessage WorkspaceNotFound = new(
        "invite.error.workspaceNotFound",
        "Пространство не найдено");

    /// <summary>
    /// Приглашение с пустым набором прав не даёт даже войти в пространство: членство —
    /// это и есть строки прав (спец. authorization, 1). Видно из самого запроса —
    /// <c>422</c> у поля <c>permissions</c> (<c>InvitationCreateDto.Validate</c>)
    /// </summary>
    public static readonly ErrorMessage EmptyPermissions = new(
        "invite.error.emptyPermissions",
        "Приглашение без прав не даёт войти в пространство");

    public static readonly ErrorMessage NotFound = new(
        "invite.error.notFound",
        "Приглашение не найдено");

    /// <summary>
    /// Отзываем только непринятую ссылку: участника из пространства отзыв не выкинет,
    /// и кнопка обещала бы не то, что делает
    /// </summary>
    public static readonly ErrorMessage CannotRevokeAccepted = new(
        "invite.error.cannotRevokeAccepted",
        "Принятое приглашение отозвать нельзя");
}
