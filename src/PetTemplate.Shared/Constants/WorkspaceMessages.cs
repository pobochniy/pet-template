using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Constants;

/// <summary>
/// Отказы по составу пространства (спец. authorization, 5 и 8). Лежат рядом
/// с <see cref="InvitationMessages"/> и по той же причине: клиент показывает
/// их из тела ответа, и вторая формулировка на клиенте разъехалась бы.
/// Перевод — словарь <c>workspace</c> (<c>i18n/{lang}/workspace.json</c>)
/// </summary>
public static class WorkspaceMessages
{
    public static readonly ErrorMessage NotAMember = new(
        "workspace.error.notAMember",
        "Вы не участник этого пространства");

    public static readonly ErrorMessage MemberNotFound = new(
        "workspace.error.memberNotFound",
        "Участник не найден в этом пространстве");

    /// <summary>
    /// Себя убирает «Выйти», а не удаление участника: выход отвечает новым токеном
    /// и переводит указатель основного пространства, а удаление — ни того, ни другого
    /// </summary>
    public static readonly ErrorMessage CannotRemoveSelf = new(
        "workspace.error.cannotRemoveSelf",
        "Себя из пространства убирает кнопка «Выйти»");

    /// <summary>
    /// Пустой набор прав — это уже не участник: в списках пространства его не будет,
    /// а строк в <c>UserInPermissions</c> не останется вовсе. Отказ виден из самого
    /// запроса, поэтому это <c>422</c> у поля <c>permissions</c>, а не <c>400</c>
    /// (<c>UserAndPermissionsDto.Validate</c>)
    /// </summary>
    public static readonly ErrorMessage EmptyPermissions = new(
        "workspace.error.emptyPermissions",
        "Оставьте участнику хотя бы одно право. " +
        "Чтобы убрать человека из пространства, удалите участника");

    /// <summary>
    /// Одно правило на два места: выдачу прав в приглашении (спец. authorization, 6.1)
    /// и правку прав участника (раздел 5). Иначе <c>PermissionManagement</c>
    /// превращается в способ выписать себе что угодно — хоть через самоприглашение,
    /// хоть галкой в строке списка участников
    /// </summary>
    public static readonly ErrorMessage PermissionsExceedOwn = new(
        "workspace.error.permissionsExceedOwn",
        "Нельзя выдать права, которых нет у вас самих");

    /// <summary>
    /// Основное пространство — свойство пользователя, поэтому единственная проверка
    /// при его смене — членство (спец. authorization, 7.1)
    /// </summary>
    public static readonly ErrorMessage WorkspaceUnavailable = new(
        "workspace.error.unavailable",
        "Пространство недоступно");

    /// <summary>
    /// Распорядитель прав в пространстве обязан остаться хотя бы один, иначе править
    /// доступы станет некому — в том числе вернуть право себе.
    /// Снять его с последнего (обычно с самого себя) не даём; передать право
    /// оставшимся умеют выход и удаление участника
    /// </summary>
    public static readonly ErrorMessage LastPermissionManager = new(
        "workspace.error.lastPermissionManager",
        "В пространстве должен остаться хотя бы один участник, который распоряжается правами. " +
        "Сначала выдайте это право кому-то ещё");
}
