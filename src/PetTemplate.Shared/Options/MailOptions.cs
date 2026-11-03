namespace PetTemplate.Shared.Options;

/// <summary>
/// Отправка почты (спец. mail, 5): SMTP, а какой у него провайдер — дело настроек.
/// Пароль — только переменной окружения <c>MailOptions__Password</c> из секрета
/// <c>SMTP_PASSWORD</c>. Секция — только в <c>appsettings.Release.json</c>.
/// Пустой <see cref="Host"/> — почта выключена: письма не уходят, а пишутся в лог
/// </summary>
public class MailOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 465;

    public bool UseSsl { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = "info@example.com";

    public string FromName { get; set; } = "PetTemplate";

    /// <summary>Адрес сайта для ссылок в письмах — <c>/auth/reset/…</c>, <c>/auth/confirm-email/…</c></summary>
    public string PublicBaseUrl { get; set; } = "https://example.com";
}
