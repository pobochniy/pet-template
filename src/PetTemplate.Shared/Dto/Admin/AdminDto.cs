using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto.Admin;

/// <summary>
/// Кто вошёл в админку и с какими ролями (спец. admin, 3.3 и 6) — для оболочки
/// клиента: вкладки видны по ролям. Роли — те, что в токене и ещё лежат в базе: проверка
/// токена урезает их на каждом запросе (3.3), снятая роль пропадает сразу
/// </summary>
public class AdminDto
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = null!;

    /// <summary>По роли на элемент, без склеенных флагов — так их читает клиент</summary>
    public AdminRole[] Roles { get; set; } = [];
}
