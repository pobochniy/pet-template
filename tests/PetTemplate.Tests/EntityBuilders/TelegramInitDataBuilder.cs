using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace PetTemplate.Tests.EntityBuilders;

public class TelegramInitDataBuilder
{
    private long _userId = 12345678;
    private string _firstName = "Test";
    private string? _lastName = "User";
    private string? _username = "testuser";
    private string? _languageCode = "en";
    private string _botToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
    private DateTimeOffset? _authDate;
    private string? _startParam;

    public TelegramInitDataBuilder WithUserId(long userId)
    {
        _userId = userId;
        return this;
    }

    public TelegramInitDataBuilder WithFirstName(string firstName)
    {
        _firstName = firstName;
        return this;
    }

    public TelegramInitDataBuilder WithLastName(string? lastName)
    {
        _lastName = lastName;
        return this;
    }

    public TelegramInitDataBuilder WithUsername(string? username)
    {
        _username = username;
        return this;
    }

    public TelegramInitDataBuilder WithLanguageCode(string? languageCode)
    {
        _languageCode = languageCode;
        return this;
    }

    /// <summary>
    /// Когда Telegram подписал параметры (<c>auth_date</c>). По умолчанию — сейчас;
    /// раньше — чтобы проверить свежесть подписи (спец. accounts, 5.6)
    /// </summary>
    public TelegramInitDataBuilder SignedAt(DateTimeOffset authDate)
    {
        _authDate = authDate;
        return this;
    }

    /// <summary>
    /// Mini-app открыт по ссылке-приглашению: <c>start_param=inv_&lt;токен&gt;</c> — развилки
    /// нет, аккаунт заводится сразу (спец. accounts, 5.6)
    /// </summary>
    public TelegramInitDataBuilder FromInvitation(string token = "abc")
    {
        _startParam = $"inv_{token}";
        return this;
    }

    public TelegramInitDataBuilder WithBotToken(string botToken)
    {
        _botToken = botToken;
        return this;
    }

    public string Please()
    {
        var user = new TelegramUserData
        {
            Id = _userId,
            FirstName = _firstName,
            LastName = _lastName,
            Username = _username,
            LanguageCode = _languageCode
        };

        var userJson = JsonSerializer.Serialize(user);
        var authDate = (_authDate ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

        // Создаем параметры (без hash)
        var parameters = new Dictionary<string, string>
        {
            { "user", userJson },
            { "auth_date", authDate.ToString() }
        };
        if (_startParam != null) parameters["start_param"] = _startParam;

        // Сортируем и создаем строку для подписи
        var dataCheckString = string.Join("\n",
            parameters
                .OrderBy(p => p.Key)
                .Select(p => $"{p.Key}={p.Value}"));

        // Вычисляем hash
        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(_botToken));

        var hash = HMACSHA256.HashData(
            secretKey,
            Encoding.UTF8.GetBytes(dataCheckString));

        var hashHex = Convert.ToHexString(hash).ToLower();

        // Формируем финальную строку initData
        parameters.Add("hash", hashHex);

        return string.Join("&", parameters.Select(p => $"{p.Key}={HttpUtility.UrlEncode(p.Value)}"));
    }

    private sealed class TelegramUserData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;
        
        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }
        
        [JsonPropertyName("username")]
        public string? Username { get; set; }
        
        [JsonPropertyName("language_code")]
        public string? LanguageCode { get; set; }
    }
}
