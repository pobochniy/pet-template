namespace PetTemplate.Shared.Dto;

/// <summary>
/// Вход из mini-app VK — <c>Auth/VkAuth</c> (спец. accounts, 5.6)
/// </summary>
public class VkAuthDto
{
    /// <summary>
    /// Строка запроса, с которой VK открыл mini-app (<c>vk_user_id=…&amp;vk_ts=…&amp;sign=…</c>), —
    /// как есть, без разбора на поля. VK подписывает все параметры <c>vk_*</c>, и набор их
    /// растёт: поле, которого нет в DTO, выпало бы из проверки, и подпись перестала бы сходиться.
    /// https://dev.vk.com/ru/mini-apps/development/launch-params
    /// </summary>
    public string? LaunchParams { get; set; }

    /// <summary>
    /// Приглашение из фрагмента запуска <c>vk.com/app&lt;id&gt;#inv_…</c>. Фрагмент VK не
    /// подписывает, поэтому сервер верит не самому параметру, а живому приглашению с этим
    /// токеном: с ним развилки нет, как у Telegram
    /// </summary>
    public Guid? Invitation { get; set; }

    /// <summary>
    /// Язык интерфейса для нового профиля, если VK не назвал свой (<c>vk_language</c>)
    /// </summary>
    public string? Language { get; set; }
}
