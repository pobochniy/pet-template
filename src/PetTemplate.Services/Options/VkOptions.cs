namespace PetTemplate.Services.Options;

/// <summary>
/// Mini-app VK (спец. accounts, 5.6): приложение типа «VK Mini Apps», не «Веб» для входа
/// VK ID (<c>VkIdOptions</c>)
/// </summary>
public class VkOptions
{
    /// <summary>Id mini-app — ссылки <c>vk.com/app&lt;id&gt;#…</c>; 0 — mini-app нет</summary>
    public int AppId { get; set; }

    /// <summary>
    /// Защищённый ключ — им VK подписывает параметры запуска. Секрет: на сервере —
    /// переменной окружения <c>VkOptions__PrivateKey</c>. Пусто — вход из mini-app VK закрыт
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;
}
