using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Interfaces;
using PetTemplate.Services.Options;

namespace PetTemplate.Services.Services;

public class VkService(IOptions<VkOptions> options, ApplicationContext db) : IVkService
{
    private readonly VkOptions _options = options.Value;

    public bool IsSignValid(string queryString)
    {
        var queryParameters = HttpUtility.ParseQueryString(queryString);
        var orderedKeys = queryParameters
            .AllKeys
            .Where(p => p != null && p.StartsWith("vk_"))
            .OrderBy(p => p);

        var signParams = new Dictionary<string, string>();
        foreach (var key in orderedKeys)
        {
            signParams[key] = queryParameters[key];
        }

        var signString = string.Join("&", signParams
            .OrderBy(x => x.Key)
            .Select(x => x.Key + "=" + WebUtility.UrlEncode(x.Value)));
        var token = HmacHash(signString, _options.PrivateKey);
        var isRequestValid = token.Equals(queryParameters["sign"]);
        return isRequestValid;
    }

    // public async Task<(long, string)?> GetAuthToken(Guid userId)
    // {
    //     var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId);
    //     if (user == null) return null;
    //     return (user.VkId.Value, user.VkAuthToken);
    // }

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