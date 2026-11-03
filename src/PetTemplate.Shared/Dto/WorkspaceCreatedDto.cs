namespace PetTemplate.Shared.Dto;

/// <summary>
/// Ответ на создание пространства: id нового пространства и новая сессия
/// (спец. authorization, 2.2 и 3, п. 5).
///
/// Сессия обязательна именно здесь: создание выдаёт создателю полный набор прав
/// в пространстве, о котором клиент иначе не узнал бы до следующего входа. Сервер
/// их видит и так — права он читает из базы (спец. authorization, 2.3). Отдельный вызов
/// «за сессией» мог не случиться, и меню не знало бы о новом пространстве
/// </summary>
public class WorkspaceCreatedDto
{
    public Guid WorkspaceId { get; set; }

    public AuthenticationResultDto Authentication { get; set; } = null!;
}
