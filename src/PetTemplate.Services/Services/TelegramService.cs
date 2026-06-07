using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Configuration;

namespace PetTemplate.Services.Services;

public class TelegramService(IConfiguration configuration)
{
    private readonly string _botToken = configuration["Telegram:BotToken"] 
                                        ?? throw new InvalidOperationException("Telegram:BotToken not configured");

    /// <summary>
    /// Проверяет валидность подписи Telegram WebApp initData
    /// </summary>
    public bool ValidateInitData(string? initData)
    {
        try
        {
            if (initData == null) return false;
            var parameters = HttpUtility.ParseQueryString(initData);
            var hash = parameters["hash"];
            
            if (string.IsNullOrEmpty(hash))
                return false;

            // Удаляем hash из параметров для проверки
            parameters.Remove("hash");

            // Сортируем параметры и создаем строку для проверки
            var dataCheckString = string.Join("\n", 
                parameters.AllKeys
                    .OrderBy(k => k)
                    .Select(k => $"{k}={parameters[k]}"));

            // Создаем secret key из bot token
            var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(_botToken));
            
            // Вычисляем HMAC-SHA256
            var computedHash = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
            var computedHashHex = Convert.ToHexString(computedHash).ToLower();

            return computedHashHex == hash;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Извлекает Telegram User ID из initData
    /// </summary>
    public long? ExtractTelegramUserId(string initData)
    {
        try
        {
            var parameters = HttpUtility.ParseQueryString(initData);
            var userJson = parameters["user"];
            
            if (string.IsNullOrEmpty(userJson))
                return null;

            // Парсим JSON вручную для получения id
            var userData = System.Text.Json.JsonSerializer.Deserialize<TelegramUser>(userJson);
            return userData?.Id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Извлекает данные пользователя Telegram из initData
    /// </summary>
    public TelegramUser? ExtractTelegramUser(string initData)
    {
        try
        {
            var parameters = HttpUtility.ParseQueryString(initData);
            var userJson = parameters["user"];
            
            if (string.IsNullOrEmpty(userJson))
                return null;

            return System.Text.Json.JsonSerializer.Deserialize<TelegramUser>(userJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Генерирует тестовый initData с валидной подписью для разработки
    /// </summary>
    public string GenerateTestInitData(string username, long userId)
    {
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var userJson = System.Text.Json.JsonSerializer.Serialize(new TelegramUser
        {
            Id = userId,
            FirstName = username,
            Username = username
        });

        var parameters = new Dictionary<string, string>
        {
            { "auth_date", authDate.ToString() },
            { "user", userJson }
        };

        var dataCheckString = string.Join("\n",
            parameters.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(_botToken));
        var computedHash = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
        var hash = Convert.ToHexString(computedHash).ToLower();

        return $"auth_date={authDate}&user={Uri.EscapeDataString(userJson)}&hash={hash}";
    }

    public class TelegramUser
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
        
        [JsonPropertyName("is_premium")]
        public bool? IsPremium { get; set; }
    }
}