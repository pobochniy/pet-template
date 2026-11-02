namespace PetTemplate.Shared.Options;

/// <summary>
/// Настройки админки (спец. admin, 7): своя схема JWT и лимит попыток входа.
///
/// Схема своя целиком — своя аудитория и свой ключ подписи, а не клейм в токене
/// продукта (3.3): токен продукта админку не открывает, админский бесполезен
/// в продукте. Совпадение ключа или аудитории с <c>JwtOptions</c> роняет старт.
///
/// Первого админа конфигурация не заводит (3.1) — ключа под это здесь нет намеренно.
/// В <c>appsettings.json</c> — примеры; <see cref="SigningKey"/> на сервере приезжает
/// переменной окружения <c>AdminOptions__SigningKey</c> из секрета <c>ADMIN_JWT_SIGNING_KEY</c>.
/// </summary>
public class AdminOptions
{
    public string Issuer { get; set; } = "PetTemplate.Admin";

    public string Audience { get; set; } = "PetTemplate.Admin";

    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Срок токена — час, без продления (3.3): панели, которой пользуются раз в неделю
    /// по десять минут, вечная сессия не нужна, а час — приемлемая задержка снятия роли
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Кука для шлюза перед сторонними инструментами (3.3, Grafana): тот же токен,
    /// что в ответе входа, и тот же срок. Её читает только <c>Admin/Auth/Verify</c> —
    /// ни один изменяющий метод по ней не авторизуется
    /// </summary>
    public string GatewayCookieName { get; set; } = "pettemplate_admin";

    /// <summary>
    /// Домен куки. На проде — <c>.example.com</c> (<c>appsettings.Release.json</c>):
    /// иначе её не увидит <c>grafana.example.com</c>. Пусто — кука только своего хоста,
    /// так на <c>localhost</c>
    /// </summary>
    public string GatewayCookieDomain { get; set; } = string.Empty;

    /// <summary>
    /// Сколько неудачных входов за <see cref="AdminLoginWindow"/> прощается — по учётке
    /// и по адресу отдельно (3.3). Дальше вход отвечает <c>404</c>, даже с верным паролем
    /// </summary>
    public int AdminLoginAttempts { get; set; } = 5;

    public TimeSpan AdminLoginWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Сколько живёт ссылка-приглашение админа (3.4) — как у пространства. Чистка
    /// уносит строку через 21 день после выдачи, как и там
    /// </summary>
    public TimeSpan AdminInviteTtl { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// Сколько живёт журнал доступа к диалогам (спец. admin, 16.2) — год, дольше трассы:
    /// на вопрос «кто читал мою переписку в марте» ответ должен найтись и в мае
    /// </summary>
    public int AccessLogRetentionDays { get; set; } = 365;

    /// <summary>
    /// Сколько живёт автооценка диалога (16.3) — год: переписки в ней нет, а по ней видно,
    /// стало ли за полгода лучше
    /// </summary>
    public int EvaluationRetentionDays { get; set; } = 365;

    /// <summary>Автооценка диалогов (16.3)</summary>
    public DialogEvaluationOptions Evaluation { get; set; } = new();
}

/// <summary>Настройки автооценки диалогов (спец. admin, 16.3)</summary>
public class DialogEvaluationOptions
{
    /// <summary>Потолок пачки одного запуска — и потолок расхода одного нажатия</summary>
    public int BatchSize { get; set; } = 20;
}
