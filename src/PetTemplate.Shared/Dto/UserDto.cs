using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto;

public class UserDto
{
    public Guid Id { get; set; }

    public ProfileDto Profile { get; set; } = null!;

    /// <summary>
    /// Права по пространствам (спец. authorization, 2.1): ключ — пространство,
    /// значение — права в нём. Пространства без прав нет — членство и есть права
    /// </summary>
    public Dictionary<Guid, List<PermissionEnum>> PermissionsByWorkspace { get; set; } = new();

    /// <summary>
    /// Аккаунт только что заведён входом через Telegram или VK (спец. accounts, 5.4 и 5.5):
    /// клиент ведёт на «Добро пожаловать» (<c>/welcome</c>)
    /// </summary>
    public bool IsNewAccount { get; set; }
}
