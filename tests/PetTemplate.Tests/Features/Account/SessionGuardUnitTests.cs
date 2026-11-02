using Microsoft.Extensions.Caching.Memory;
using PetTemplate.Services.Services;

namespace PetTemplate.Tests.Features.Account;

/// <summary>
/// Кэш отметки отзыва (спец. accounts, 8.1 и 11): значение, прочитанное из базы до
/// сброса (<see cref="SessionGuard.Forget"/>), не должно лечь в кэш после него — иначе
/// токен, выданный до смены пароля, жил бы ещё <see cref="SessionGuard.CacheTtl"/>
/// </summary>
public class SessionGuardUnitTests
{
    private static readonly Guid OlegId = Guid.CreateVersion7();
    private static readonly DateTime TokenIssuedAt = DateTime.UtcNow.AddMinutes(-10);

    /// <summary>
    /// «База» в памяти. <see cref="DuringNextLoad"/> срабатывает, когда чтение уже вернуло
    /// старое значение, но до того, как его положили в кэш: так смена пароля вклинивается
    /// ровно в окно гонки
    /// </summary>
    private sealed class Guard(IMemoryCache cache) : SessionGuard(null!, cache)
    {
        public DateTime? ValidAfterUtc { get; set; }

        public Action? DuringNextLoad { get; set; }

        protected override Task<Entry> Load(Guid userId, CancellationToken ct)
        {
            var entry = new Entry(true, ValidAfterUtc);

            var during = DuringNextLoad;
            DuringNextLoad = null;
            during?.Invoke();

            return Task.FromResult(entry);
        }
    }

    [Fact]
    public async Task ValueReadBeforeForgetDoesNotStayCached()
    {
        var guard = new Guard(new MemoryCache(new MemoryCacheOptions()));
        guard.DuringNextLoad = () =>
        {
            // Смена пароля: отметка записана, кэш сброшен — пока первое чтение ещё в пути
            guard.ValidAfterUtc = DateTime.UtcNow;
            guard.Forget(OlegId);
        };
        await guard.IsValid(OlegId, TokenIssuedAt);

        var valid = await guard.IsValid(OlegId, TokenIssuedAt);

        Assert.False(valid);
    }

    /// <summary>Без сброса прочитанное кэшируется: следующий запрос в базу не ходит</summary>
    [Fact]
    public async Task ValueIsCachedWithoutForget()
    {
        var guard = new Guard(new MemoryCache(new MemoryCacheOptions()));
        await guard.IsValid(OlegId, TokenIssuedAt);
        guard.ValidAfterUtc = DateTime.UtcNow;

        var valid = await guard.IsValid(OlegId, TokenIssuedAt);

        Assert.True(valid);
    }

    /// <summary>Токен без <c>iat</c> (выпущен до появления отзыва) действует, пока отзывов не было</summary>
    [Fact]
    public async Task TokenWithoutIssuedAtIsValidUntilRevocation()
    {
        var guard = new Guard(new MemoryCache(new MemoryCacheOptions()));

        var valid = await guard.IsValid(OlegId, null);

        Assert.True(valid);
    }
}
