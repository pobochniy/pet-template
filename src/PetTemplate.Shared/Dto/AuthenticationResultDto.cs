using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Ответ любого входа и любого перевыпуска сессии (спец. authorization, 2.1–2.2):
/// пароль, Telegram, VK, приглашение, <c>Workspace/Create</c>, <c>Workspace/Leave</c>.
/// Собирается в одном месте — <c>AuthResultFactory</c>
/// </summary>
public class AuthenticationResultDto
{
    public string Token { get; set; } = null!;

    public ProfileDto Profile { get; set; } = null!;

    /// <summary>
    /// Те же права, что в токене, — словарём по пространствам. Клиент по ним рисует
    /// меню и селектор пространств, не разбирая JWT
    /// </summary>
    public Dictionary<Guid, List<PermissionEnum>> PermissionsByWorkspace { get; set; } = new();

    /// <summary>
    /// Аккаунт только что заведён входом через Telegram или VK (спец. accounts, 5.4 и 5.5):
    /// клиент ведёт на «Добро пожаловать» (<c>/welcome</c>)
    /// </summary>
    public bool IsNewAccount { get; set; }

    /// <summary>
    /// «У меня уже есть аккаунт» в mini-app (<c>Auth/LogInAndLink</c>), а вход площадки
    /// уже принадлежит другому аккаунту: привязки нет, занятый вход ведёт в слияние
    /// (спец. accounts, раздел 3, 7.1). Сессия — аккаунта по паролю, клиент открывает
    /// экран слияния с этим просмотром. У остальных входов — <c>null</c>
    /// </summary>
    public MergePreviewDto? MergePreview { get; set; }
}
