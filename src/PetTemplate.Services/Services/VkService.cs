using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Interfaces;
using PetTemplate.Services.Options;

namespace PetTemplate.Services.Services;

public class VkService(IOptions<VkOptions> options) : IVkService
{
    private readonly VkOptions _options = options.Value;

    /// <summary>
    /// Подпись параметров запуска mini-app: HMAC-SHA256 защищённым ключом приложения по всем
    /// <c>vk_*</c>, отсортированным по имени (https://dev.vk.com/ru/mini-apps/development/launch-params-sign).
    /// Ключ не задан — подпись не сходится ни у кого
    /// </summary>
    public bool IsSignValid(string queryString)
    {
        if (string.IsNullOrEmpty(_options.PrivateKey)) return false;

        var queryParameters = HttpUtility.ParseQueryString(queryString);
        var orderedKeys = queryParameters
            .AllKeys
            .Where(p => p != null && p.StartsWith("vk_"))
            .OrderBy(p => p);

        var signParams = new Dictionary<string, string>();
        foreach (var key in orderedKeys)
        {
            signParams[key!] = queryParameters[key] ?? string.Empty;
        }

        var signString = string.Join("&", signParams
            .OrderBy(x => x.Key)
            .Select(x => x.Key + "=" + WebUtility.UrlEncode(x.Value)));
        var token = HmacHash(signString, _options.PrivateKey);
        var sign = queryParameters["sign"];
        return sign != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(sign));
    }

    private static string HmacHash(string message, string secret)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var keyBytes = Encoding.UTF8.GetBytes(secret);

        using var hash = new HMACSHA256(keyBytes);
        var hashMessage = hash.ComputeHash(messageBytes);
        return Convert
            .ToBase64String(hashMessage)
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", string.Empty);
    }
}
