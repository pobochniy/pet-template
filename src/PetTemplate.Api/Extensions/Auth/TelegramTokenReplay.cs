using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Одноразовость <c>id_token</c> Telegram (спец. accounts, 5.2). Токен принимается в
/// пределах <c>TelegramLoginMaxAge</c> от <c>iat</c>, и без учёта подсмотренный выдавал бы
/// новую сессию на каждое предъявление. Помнится хеш токена — до конца его срока приёма.
///
/// Счёт в памяти процесса, как у <see cref="ResetIpLimiter"/>: API один, после рестарта
/// токен ещё раз пройдёт в пределах своих минут. Кэш с потолком записей; переполнен —
/// выбрасывается четверть, начиная с истёкших
/// </summary>
public class TelegramTokenReplay : IDisposable
{
    public const int MaxEntries = 100_000;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });
    private readonly Lock _lock = new();

    /// <summary>Отметить токен израсходованным; <c>false</c> — его уже предъявляли</summary>
    public bool TryUse(string idToken, TimeSpan remember)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idToken)));
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out _)) return false;
            if (_cache.Count >= MaxEntries) _cache.Compact(0.25);

            _cache.Set(key, true, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = remember });
            return true;
        }
    }

    public void Dispose() => _cache.Dispose();
}
