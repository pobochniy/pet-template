using System.Globalization;
using PetTemplate.Shared.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Вход через Telegram на сайте — «Log In with Telegram» во всплывающем окне (спец.
/// accounts, 5.2). Один на вход (<c>Auth/TelegramLogin</c>) и привязку (<c>Account/Link</c>).
///
/// Окно Telegram отдаёт странице <c>id_token</c> — JWT, подписанный ключом Telegram; клиент
/// передаёт его сюда как есть. Проверка — издатель, аудитория (id бота), срок, подпись
/// и <c>nonce</c>: им клиент назвал свой <c>state</c>, так что токен, выданный в чужом
/// браузере, сюда не подсунуть. Сервер в Telegram не ходит, поэтому ни обмена кода,
/// ни запроса ключей: ключи — копией в <see cref="TelegramLoginOptions.Keys"/>.
///
/// Id — claim <c>id</c>, а не <c>sub</c>: <c>sub</c> у Telegram — свой идентификатор OIDC,
/// а <c>id</c> (scope <c>profile</c>) — числовой id пользователя Telegram. Срок —
/// <see cref="AccountOptions.TelegramLoginMaxAge"/> от <c>iat</c>; токен одноразовый
/// (<see cref="TelegramTokenReplay"/>)
/// </summary>
public class TelegramLogin(
    IOptions<TelegramLoginOptions> loginOptions,
    IOptions<AccountOptions> options,
    TelegramTokenReplay replay,
    ILogger<TelegramLogin> logger)
{
    private TelegramLoginOptions LoginOptions => loginOptions.Value;

    public bool IsEnabled => !string.IsNullOrEmpty(LoginOptions.BotId);

    /// <summary>
    /// Пользователь Telegram по <c>id_token</c> из окна входа и <c>state</c>, который клиент
    /// передал окну как <c>nonce</c>. Токен не прошёл проверку — <c>null</c>
    /// </summary>
    public async Task<ExternalIdentity?> Resolve(string idToken, string? state)
    {
        if (!IsEnabled) return null;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = LoginOptions.Issuer,
            ValidAudience = LoginOptions.BotId,
            IssuerSigningKeys = SigningKeys(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        });

        if (!result.IsValid)
        {
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                // Telegram сменил ключи — копию в TelegramLoginOptions:Keys пора обновить
                logger.LogError("Telegram id_token is signed by an unknown key {Kid}: update TelegramLoginOptions:Keys from jwks.json",
                    (result.SecurityToken as JsonWebToken)?.Kid);
            }
            else
            {
                logger.LogWarning(result.Exception, "Telegram id_token is invalid");
            }

            return null;
        }

        var token = (JsonWebToken)result.SecurityToken;

        if (string.IsNullOrEmpty(state) || Claim(result, "nonce") != state)
        {
            logger.LogWarning("Telegram id_token nonce does not match the login state");
            return null;
        }

        var age = DateTimeOffset.UtcNow - new DateTimeOffset(token.IssuedAt, TimeSpan.Zero);
        if (token.IssuedAt == DateTime.MinValue || age > options.Value.TelegramLoginMaxAge || age < TimeSpan.FromMinutes(-1))
        {
            logger.LogWarning("Telegram id_token is stale: iat={IssuedAt}", token.IssuedAt);
            return null;
        }

        if (!long.TryParse(Claim(result, "id"), out var id) || id <= 0)
        {
            logger.LogWarning("Telegram id_token carries no numeric id");
            return null;
        }

        // Последней: место в учёте занимают только токены, прошедшие все проверки. Помнить —
        // пока он принимался бы: срок от iat плюс допуск часов
        if (!replay.TryUse(idToken, options.Value.TelegramLoginMaxAge + TimeSpan.FromMinutes(1) - age))
        {
            logger.LogWarning("Telegram id_token is replayed: id={Id}", id);
            return null;
        }

        // iat чуть из будущего (часы разошлись) — не дальше «сейчас»: окно не длиннее положенного
        var issuedAt = new DateTimeOffset(token.IssuedAt, TimeSpan.Zero);
        var confirmedAt = issuedAt < DateTimeOffset.UtcNow ? issuedAt : DateTimeOffset.UtcNow;
        return new ExternalIdentity(id, Claim(result, "preferred_username"), confirmedAt);
    }

    private static string? Claim(TokenValidationResult result, string name) =>
        result.Claims.TryGetValue(name, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private List<SecurityKey> SigningKeys() => LoginOptions.Keys
        .Select(x => (SecurityKey)new JsonWebKey
        {
            Kid = x.Kid,
            Kty = x.Kty,
            Alg = x.Alg,
            N = x.N,
            E = x.E,
            Crv = x.Crv,
            X = x.X,
            Y = x.Y
        })
        .ToList();
}
