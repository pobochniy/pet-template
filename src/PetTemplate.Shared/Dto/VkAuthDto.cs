namespace LA.Atheneum.Dto.Auth;

/// <summary>
/// https://dev.vk.com/ru/mini-apps/development/launch-params?ref=old_portal
/// </summary>
public class VkAuthDto
{
    /// <summary>
    /// Идентификатор пользователя, запустившего мини-приложение.
    /// </summary>
    public long vk_user_id { get; set; }
    
    /// <summary>
    /// Идентификатор мини-приложения.
    /// </summary>
    public int vk_app_id { get; set; }
    
    /// <summary>
    /// Идентификатор чата. Передаётся, если приложение было запущено из чата.
    /// </summary>
    public string? vk_chat_id { get; set; }
    
    /// <summary>
    /// Информация об установке пользователем мини-приложения:
    /// </summary>
    public byte? vk_is_app_user { get; set; }
    
    /// <summary>
    /// Информация о разрешённых уведомлениях от мини-приложения:
    /// </summary>
    public byte? vk_are_notifications_enabled { get; set; }
    
    /// <summary>
    /// Язык пользовательского интерфейса мини-приложения (2 символа)
    /// </summary>
    public string vk_language { get; set; }
    
    /// <summary>
    /// Источник запуска мини-приложения.
    /// </summary>
    public string vk_ref { get; set; }
    
    /// <summary>
    /// Список прав доступа текущего пользователя в мини-приложении. Например: "friends, video, photos".
    /// </summary>
    public string? vk_access_token_settings { get; set; }
    
    /// <summary>
    /// Идентификатор сообщества, если мини-приложение запущено из сообщества. Информация о группе возвращается, если:
    /// - Мини-приложение запущено с помощью кнопок в сообществе или из блока приложений сообщества.
    /// - Исходная ссылка на мини-приложение имела вид: vk.com/app12345_-12345.
    /// </summary>
    public int? vk_group_id { get; set; }
    
    /// <summary>
    /// Роль пользователя в сообществе, из которого запущено мини-приложение (если есть vk_group_id)
    /// </summary>
    public string? vk_viewer_group_role { get; set; }
    
    /// <summary>
    /// Платформа и приложение, из которого было запущено мини-приложение.
    /// </summary>
    public string vk_platform { get; set; }
    
    /// <summary>
    /// Информация о добавлении мини-приложения в избранное:
    /// </summary>
    public byte? vk_is_favorite { get; set; }
    
    /// <summary>
    /// Время генерации подписи параметров запуска. Подпись передаётся в параметре sign
    /// </summary>
    public int vk_ts { get; set; }
    
    /// <summary>
    /// Информация о том, порекомендовано ли мини-приложение:
    /// </summary>
    public byte? vk_is_recommended { get; set; }
    
    /// <summary>
    /// Идентификатор пользователя, по кнопке в профиле которого запускается мини-приложение.
    /// Если мини-приложение запущено не по кнопке в профиле, параметр не передаётся
    /// </summary>
    public string? vk_profile_id { get; set; }
    
    /// <summary>
    /// Информация о том, установил ли пользователь кнопку в профиле.
    /// Значение 1 означает, что пользователь установил кнопку в профиле, иначе параметр не передаётся.
    /// </summary>
    public bool? vk_has_profile_button { get; set; }
    
    /// <summary>
    /// Идентификатор тестовой группы, в которой состоит пользователь.
    /// Если пользователь не состоит в тестовой группе, параметр не передаётся.
    /// </summary>
    public int? vk_testing_group_id { get; set; }
    
    /// <summary>
    /// Подпись переданных параметров. Нужна, чтобы убедиться, что параметры валидны и не были модифицированы при передаче.
    /// </summary>
    public string sign { get; set; }
    
    /// <summary>
    /// Информация о запуске мини-приложения в режиме ODR.
    /// Значение 1 означает, что мини-приложение запущено в режиме ODR, иначе параметр не передаётся
    /// </summary>
    public byte? odr_enabled { get; set; }
    
    
    // public string vk_api_url { get; set; }
    // public int vk_api_id { get; set; }
    // public string vk_api_settings { get; set; }
    // public int vk_viewer_id { get; set; }
    // public string vk_viewer_type { get; set; }
    // public string vk_access_token { get; set; }
    // public string sign_keys { get; set; }
    // public int timestamp { get; set; }
    // public string auth_key { get; set; }
    // public int parent_language { get; set; }
    // public int is_secure { get; set; }
    // public string api_result { get; set; }
    // public string referrer { get; set; }
    // public string hash { get; set; }
}