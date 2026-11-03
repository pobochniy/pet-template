namespace PetTemplate.Shared.Options;

/// <summary>
/// Вход через Telegram на сайте — «Log In with Telegram» во всплывающем окне (спец.
/// accounts, 5.2). <c>client_id</c> — id бота проекта (<see cref="BotId"/>): своих секретов
/// у входа нет, токен бота серверу для входа не нужен (он нужен только mini-app —
/// <c>Telegram:BotToken</c>). Адрес сайта и адрес возврата задаются боту в BotFather
/// (Login Widget → Allowed URLs)
/// </summary>
public class TelegramLoginOptions
{
    /// <summary>
    /// Id бота — начало его токена до двоеточия; публичный, это <c>client_id</c> окна входа
    /// и аудитория <c>id_token</c>. Пусто — вход через Telegram выключен, кнопки нет
    /// </summary>
    public string BotId { get; set; } = string.Empty;

    /// <summary>Страница входа Telegram — её открывает окно в браузере, сервер её не вызывает</summary>
    public string AuthorizationEndpoint { get; set; } = "https://oauth.telegram.org/auth";

    /// <summary>Адрес возврата, заведённый боту в BotFather: Telegram сверяет с ним вход</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Издатель <c>id_token</c></summary>
    public string Issuer { get; set; } = "https://oauth.telegram.org";

    /// <summary>
    /// Открытые ключи подписи <c>id_token</c> — копия <c>oauth.telegram.org/.well-known/jwks.json</c>.
    /// Копия, а не запрос (спец. accounts, 5.2 и 14, вопрос 1): сервер в Telegram не ходит. Telegram
    /// сменил ключи — вход падает с незнакомым <c>kid</c> в логе, и копию обновляют вручную
    /// </summary>
    public List<SigningKeyOptions> Keys { get; set; } = [];
}

/// <summary>
/// Ключ из JWKS площадки — поля как в JWK: RSA — <c>n</c>, <c>e</c>; EC — <c>crv</c>, <c>x</c>, <c>y</c>
/// </summary>
public class SigningKeyOptions
{
    public string Kid { get; set; } = string.Empty;
    public string Kty { get; set; } = string.Empty;
    public string Alg { get; set; } = string.Empty;
    public string? N { get; set; }
    public string? E { get; set; }
    public string? Crv { get; set; }
    public string? X { get; set; }
    public string? Y { get; set; }
}
