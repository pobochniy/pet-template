using System.ComponentModel.DataAnnotations;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Что площадка вернула после входа на сайте (спец. accounts, 5.2 и 5.3). Telegram —
/// <c>id_token</c> окна входа в <see cref="Code"/> и его <c>nonce</c> в <see cref="State"/>; VK ID — код
/// и PKCE-<see cref="Verifier"/>, который клиент хранил в <c>sessionStorage</c> до возврата
/// </summary>
public class ExternalCodeDto
{
    /// <summary><c>id_token</c> окна Telegram или код VK ID</summary>
    public string? Code { get; set; }

    /// <summary>Только VK ID: PKCE-verifier к коду. Без него VK код не обменяет</summary>
    public string? Verifier { get; set; }

    /// <summary>Только VK ID: устройство, на котором выдан код</summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// <c>state</c> входа: VK ждёт его при обмене кода, Telegram зашивает его в <c>id_token</c>
    /// как <c>nonce</c> — без совпадения вход не принимается
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// Язык интерфейса для аккаунта, который заведёт этот вход (8.4): на нём уйдут письма.
    /// Незнакомый — язык по умолчанию: его прислал браузер, а не выбрал человек
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Только войти в существующий аккаунт, не заводя нового: восстановление доступа через
    /// привязанный вход (спец. accounts, 8.2, 8.3). Неизвестный вход —
    /// <c>404 auth.error.loginNotLinked</c>
    /// </summary>
    public bool ExistingOnly { get; set; }
}

/// <summary>
/// Что клиенту нужно, чтобы отправить человека на площадку (5.1): публичные идентификаторы
/// приложений и адреса возврата. Секретов здесь нет. Пустой идентификатор — вход через эту
/// площадку выключен, и кнопки нет
/// </summary>
public class ExternalLoginsConfigDto
{
    /// <summary>Id бота — <c>client_id</c> окна входа Telegram; публичный, это начало токена до двоеточия</summary>
    public string TelegramBotId { get; set; } = string.Empty;
    public string TelegramAuthorizationEndpoint { get; set; } = string.Empty;
    public string TelegramRedirectUri { get; set; } = string.Empty;

    public string VkAppId { get; set; } = string.Empty;
    public string VkAuthorizationEndpoint { get; set; } = string.Empty;
    public string VkRedirectUri { get; set; } = string.Empty;
}

/// <summary>Развилка mini-app, «Начать» — <c>Auth/RegisterExternal</c> (спец. accounts, 5.6)</summary>
public class RegisterExternalDto
{
    [Required]
    public ExternalProofDto Proof { get; set; } = null!;

    public string? Language { get; set; }
}

/// <summary>
/// Развилка mini-app, «У меня уже есть аккаунт» — <c>Auth/LogInAndLink</c> (5.6):
/// логин и пароль и подписанный вход одним вызовом
/// </summary>
public class LogInAndLinkDto
{
    [Required]
    public string Login { get; set; } = null!;

    [Required]
    public string Password { get; set; } = null!;

    [Required]
    public ExternalProofDto Proof { get; set; } = null!;
}
