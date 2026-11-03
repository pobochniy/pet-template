using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace PetTemplate.Tests;

public static class TestSettings
{
    // Telegram Bot Token - используется для валидации InitData от Telegram Mini App
    public const string DefaultTelegramBotToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";

    /// <summary>
    /// Id бота входа через Telegram на сайте (спец. accounts, 5.2) — аудитория <c>id_token</c> в тестах
    /// </summary>
    public const string TelegramBotId = "123456";

    /// <summary>
    /// Ключ, которым «Telegram» подписывает <c>id_token</c> входа в тестах: его открытая
    /// часть встаёт в <c>TelegramLoginOptions:Keys</c> вместо копии настоящих ключей
    /// </summary>
    public static readonly RSA TelegramLoginKey = RSA.Create(2048);

    /// <summary>
    /// Открытая часть <see cref="TelegramLoginKey"/> — снята один раз. Хосты тестов
    /// поднимаются параллельно, и одновременный экспорт из общего <see cref="RSA"/> на macOS
    /// изредка отдавал не тот модуль: хост получал чужой ключ, и вход не проходил
    /// </summary>
    private static readonly RSAParameters TelegramLoginPublicKey = TelegramLoginKey.ExportParameters(false);

    public const string TelegramLoginKeyId = "test-oidc";

    /// <summary>
    /// Ключ подписи админской схемы (спец. admin, 3.3) — свой, не тот, что в
    /// <c>appsettings.json</c>, и не ключ продукта: совпадение роняет старт
    /// </summary>
    public const string AdminSigningKey = "tests-admin-signing-key-0123456789abcdef";

    public static Action<IDictionary<string, string?>> ConfigureAuth(string? telegramBotToken = null)
    {
        return settings =>
        {
            settings["Telegram:BotToken"] = telegramBotToken ?? DefaultTelegramBotToken;
            settings["JwtOptions:Issuer"] = "TestIssuer";
            settings["JwtOptions:Audience"] = "TestAudience";
            settings["JwtOptions:SigningKey"] = "TestSigningKeyThatIsLongEnough123456";
            settings["JwtOptions:ExpirationSeconds"] = "3600";
            settings["AdminOptions:SigningKey"] = AdminSigningKey;

            // Вход через Telegram на сайте — с тестовым ключом: id_token в тестах подписывается им
            foreach (var (key, value) in TelegramLogin())
            {
                settings[key] = value;
            }
        };
    }

    /// <summary>Настройки входа через Telegram: id бота и тестовый ключ первым в списке</summary>
    public static IEnumerable<KeyValuePair<string, string?>> TelegramLogin()
    {
        var key = TelegramLoginPublicKey;
        yield return new("TelegramLoginOptions:BotId", TelegramBotId);
        yield return new("TelegramLoginOptions:RedirectUri", "https://example.com/auth/telegram");
        yield return new("TelegramLoginOptions:Keys:0:Kid", TelegramLoginKeyId);
        yield return new("TelegramLoginOptions:Keys:0:Kty", "RSA");
        yield return new("TelegramLoginOptions:Keys:0:Alg", "RS256");
        yield return new("TelegramLoginOptions:Keys:0:N", Base64UrlEncoder.Encode(key.Modulus));
        yield return new("TelegramLoginOptions:Keys:0:E", Base64UrlEncoder.Encode(key.Exponent));
    }
}
