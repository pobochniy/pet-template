using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Configuration;

namespace PetTemplate.Services.Services;

/// <summary>
/// Подпись <c>initData</c> mini-app Telegram (спец. accounts, 5.6) — HMAC токеном бота.
/// Нужен только mini-app: вход на сайте проверяет <c>id_token</c> окна Telegram копией
/// открытых ключей (<c>TelegramLogin</c>) и токена бота не знает. Свежесть подписи и
/// Development без проверки — <c>MiniAppLaunch</c>
/// </summary>
public class TelegramService(IConfiguration configuration)
{
    private readonly string _botToken = configuration["Telegram:BotToken"] 
                                        ?? throw new InvalidOperationException("Telegram:BotToken not configured");

    /// <summary>
    /// Проверяет валидность подписи Telegram WebApp initData. Пустой токен бота — отказ,
    /// как у <c>VkService.IsSignValid</c>: подпись ключом из пустой строки подделает любой
    /// </summary>
    public bool ValidateInitData(string? initData)
    {
        try
        {
            if (initData == null || string.IsNullOrWhiteSpace(_botToken)) return false;
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

            // За постоянное время: побайтовое сравнение строк выходит на первом расхождении,
            // и по времени ответа подпись подбиралась бы байт за байтом
            return hash.Length == computedHash.Length * 2
                   && CryptographicOperations.FixedTimeEquals(computedHash, Convert.FromHexString(hash));
        }
        catch
        {
            return false;
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