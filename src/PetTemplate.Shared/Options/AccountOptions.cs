namespace PetTemplate.Shared.Options;

/// <summary>
/// Сроки аккаунта и входов (спец. accounts). Значения по умолчанию — те, что
/// в спецификации; секция <c>AccountOptions</c> в <c>appsettings.json</c> их повторяет
/// примером.
///
/// Сроки писем, входов Telegram и VK и слияния живут в одной секции: место
/// в настройках у них одно, как и раздел спецификации
/// </summary>
public class AccountOptions
{
    /// <summary>
    /// Сколько живёт вход через Telegram на сайте — от <c>iat</c> его <c>id_token</c> (5.2).
    /// Токен уходит на сервер сразу после входа, поэтому срок короткий: подсмотренный,
    /// он не должен оставаться входом надолго, даже если Telegram выдал его на дольше
    /// </summary>
    public TimeSpan TelegramLoginMaxAge { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Сколько живёт подпись параметров запуска mini-app — <c>auth_date</c> Telegram и
    /// <c>vk_ts</c> VK (5.6). Площадка выдаёт новую подпись на каждый запуск, а без срока
    /// подсмотренная однажды строка — вечный вход
    /// </summary>
    public TimeSpan LaunchParamsMaxAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Сколько живёт ключ слияния между просмотром и подтверждением (7.4)</summary>
    public TimeSpan MergeTicketTtl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Каким старым должен быть второй аккаунт, чтобы при слиянии остался он, а не текущий
    /// (7.3). Иначе украденная сессия сливала бы аккаунт жертвы в только что заведённый
    /// аккаунт атакующего и забирала её пространства
    /// </summary>
    public TimeSpan MergeKeepOtherMinAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Срок ссылки сброса из письма (8.2)</summary>
    public TimeSpan ResetTokenTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Срок ссылки сброса, выданной поддержкой (8.2, 8.5): её передают через человека,
    /// это дольше письма
    /// </summary>
    public TimeSpan AdminResetTokenTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Срок ссылки подтверждения почты (8.4)</summary>
    public TimeSpan EmailConfirmTokenTtl { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Окно свежего входа (8.3, 8.6): после входа паролем или через Telegram и VK открыты
    /// привязка, отвязка, смена почты и слияние, а после входа площадкой новый пароль
    /// задаётся и без старого — свежий подписанный вход доказывает владение не хуже пароля
    /// </summary>
    public TimeSpan FreshLoginWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Сколько после привязки Telegram или VK к существующему аккаунту свежий вход ими
    /// ещё не задаёт пароль без текущего и не подтверждает владение (8.3, 8.6): привязать
    /// мог и тот, кто украл сессию
    /// </summary>
    public TimeSpan RecentLinkWindow { get; set; } = TimeSpan.FromDays(1);
}
