using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PetTemplate.Tests.EntityBuilders;

/// <summary>
/// <c>id_token</c> окна «Log In with Telegram», как его отдаёт Telegram странице сайта
/// (спец. accounts, 5.2): JWT, подписанный ключом Telegram, — в тестах
/// <see cref="TestSettings.TelegramLoginKey"/>
/// </summary>
public class TelegramLoginBuilder
{
    /// <summary><c>state</c> входа, который клиент передал окну как <c>nonce</c></summary>
    public const string DefaultNonce = "state-1";

    private long _userId = 12345678;
    private string? _username = "masha";
    private string _nonce = DefaultNonce;
    private string _audience = TestSettings.TelegramBotId;
    private RSA _key = TestSettings.TelegramLoginKey;
    private DateTimeOffset _issuedAt = DateTimeOffset.UtcNow;

    public TelegramLoginBuilder WithUserId(long userId)
    {
        _userId = userId;
        return this;
    }

    public TelegramLoginBuilder WithUsername(string? username)
    {
        _username = username;
        return this;
    }

    public TelegramLoginBuilder WithNonce(string nonce)
    {
        _nonce = nonce;
        return this;
    }

    /// <summary>Токен, выданный другому боту</summary>
    public TelegramLoginBuilder WithAudience(string audience)
    {
        _audience = audience;
        return this;
    }

    /// <summary>Подписать чужим ключом — подделка</summary>
    public TelegramLoginBuilder WithKey(RSA key)
    {
        _key = key;
        return this;
    }

    public TelegramLoginBuilder WithIssuedAt(DateTimeOffset issuedAt)
    {
        _issuedAt = issuedAt;
        return this;
    }

    public string Build()
    {
        var claims = new Dictionary<string, object>
        {
            ["id"] = _userId,
            ["name"] = "Маша",
            ["nonce"] = _nonce,
            // Токен одноразовый (TelegramTokenReplay): без jti два входа в одну секунду
            // дали бы один и тот же токен — подпись RS256 детерминирована
            ["jti"] = Guid.NewGuid().ToString()
        };
        if (_username != null) claims["preferred_username"] = _username;

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://oauth.telegram.org",
            Audience = _audience,
            IssuedAt = _issuedAt.UtcDateTime,
            NotBefore = _issuedAt.UtcDateTime,
            Expires = _issuedAt.AddHours(1).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(_key) { KeyId = TestSettings.TelegramLoginKeyId }, SecurityAlgorithms.RsaSha256)
        });
    }
}
