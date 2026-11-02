namespace PetTemplate.Shared.Options;

/// <summary>
/// Вход через VK ID на сайте (спец. accounts, 5.3): приложение типа «Веб» в кабинете
/// VK ID, OAuth 2.1 + PKCE. Секрета приложения нет — его заменяет PKCE. Отдельное от
/// mini-app VK (<c>VkOptions</c>): у веб-приложения VK ID свой id и свой доверенный адрес возврата
/// </summary>
public class VkIdOptions
{
    /// <summary>Id приложения «Веб» — не секрет. Пусто — вход через VK выключен, кнопки нет</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Доверенный адрес возврата — тот, что указан в кабинете VK ID</summary>
    public string RedirectUri { get; set; } = string.Empty;

    public string AuthorizationEndpoint { get; set; } = "https://id.vk.ru/authorize";

    public string TokenEndpoint { get; set; } = "https://id.vk.ru/oauth2/auth";
}
