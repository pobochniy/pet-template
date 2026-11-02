using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Не больше <see cref="PerHour"/> запросов сброса пароля с одного адреса в час (спец.
/// accounts, 8.4). Сверх лимита ответ тот же, письмо не уходит — форма не должна
/// становиться ни рассыльщиком писем, ни подтверждателем чужих логинов.
///
/// Адрес — ключом <see cref="ClientAddress"/>: IPv6 по префиксу /64.
///
/// Счёт в памяти процесса: API один, рестарт обнуляет счётчики — это одно лишнее окно,
/// не больше. Кэш свой, с потолком записей: адресов у перебора может быть сколько угодно.
/// Адрес — из <c>X-Forwarded-For</c> (<c>ConfigureForwardedHeaders</c>), иначе у всех был
/// бы один — адрес nginx
/// </summary>
public class ResetIpLimiter : IDisposable
{
    public const int PerHour = 20;
    private const int MaxAddresses = 100_000;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxAddresses });
    private readonly Lock _lock = new();

    /// <summary>Записать попытку с адреса; <c>false</c> — лимит адреса исчерпан</summary>
    public bool TryAcquire(IPAddress? address)
    {
        var key = ClientAddress.Key(address);
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var hits = _cache.Get<List<DateTime>>(key) ?? [];
            hits.RemoveAll(x => now - x > Window);
            if (hits.Count >= PerHour) return false;

            hits.Add(now);
            _cache.Set(key, hits, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = Window });
            return true;
        }
    }

    public void Dispose() => _cache.Dispose();
}
