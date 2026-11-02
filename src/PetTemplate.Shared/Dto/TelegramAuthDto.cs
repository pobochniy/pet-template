namespace PetTemplate.Shared.Dto;

/// <summary>Вход из mini-app Telegram — <c>Auth/TelegramAuth</c> (спец. accounts, 5.6)</summary>
public class TelegramAuthDto
{
    /// <summary>Подписанный <c>initData</c>, с которым Telegram открыл mini-app</summary>
    public string? InitData { get; set; }

    /// <summary>
    /// Язык интерфейса для профиля, который заводится первым входом (8.4). Клиент mini-app
    /// берёт его из браузера. Незнакомый код заменяется языком по умолчанию, а не отбивает вход
    /// </summary>
    public string? Language { get; set; }
}
