namespace PetTemplate.Shared.Dto;

/// <summary>
/// Блок «Основное» своего профиля (спец. accounts, раздел 6): логин, способы
/// войти и контакты своего аккаунта. Способ войти — то, чем можно войти: логин без
/// пароля им не считается (раздел 3)
/// </summary>
public class AccountLoginsDto
{
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Логин подставлен сервером (4.2) — профиль предлагает его сменить плашкой
    /// </summary>
    public bool UserNameIsGenerated { get; set; }

    public bool HasPassword { get; set; }

    public string? Email { get; set; }

    /// <summary>Почта подтверждена (8.4) — только тогда по ней восстанавливают пароль</summary>
    public bool EmailConfirmed { get; set; }

    public string? Phone { get; set; }

    public string? Comment { get; set; }

    /// <summary>Привязан ли Telegram (раздел 3)</summary>
    public bool TelegramLinked { get; set; }

    /// <summary>Привязан ли VK — как <see cref="TelegramLinked"/></summary>
    public bool VkLinked { get; set; }

    /// <summary>
    /// Сколько способов войти: пароль, Telegram, VK. Последний отвязать нельзя (раздел 3)
    /// </summary>
    public int LoginCount => (HasPassword ? 1 : 0) + (TelegramLinked ? 1 : 0) + (VkLinked ? 1 : 0);

    /// <summary>
    /// Сессия открыта свежим входом через Telegram или VK (8.3), привязанным не только что
    /// (<c>AccountOptions.RecentLinkWindow</c>): новый пароль задаётся без старого. Окно
    /// свежести — <c>AccountOptions.FreshLoginWindow</c>; знает его только токен,
    /// поэтому ставит контроллер
    /// </summary>
    public bool CanSetPasswordWithoutCurrent { get; set; }

    /// <summary>
    /// До какого времени владение аккаунтом подтверждено свежим входом (8.6): паролем или
    /// площадкой, привязанной не только что. До него открыты привязка, отвязка, смена почты
    /// и слияние; <c>null</c> — сначала подтвердить паролем (<c>Account/ConfirmPassword</c>)
    /// или войти заново через Telegram или VK. Ставит контроллер — как и
    /// <see cref="CanSetPasswordWithoutCurrent"/>
    /// </summary>
    public DateTime? FreshLoginUntilUtc { get; set; }
}
