using System.Net;
using PetTemplate.Api.Extensions.Auth;

namespace PetTemplate.Tests.Features.Account;

/// <summary>Лимит запросов сброса с адреса (спец. accounts, 8.4): IPv4 — адрес, IPv6 — префикс /64</summary>
public class ResetIpLimiterUnitTests
{
    private static void Exhaust(ResetIpLimiter limiter, string address)
    {
        for (var i = 0; i < ResetIpLimiter.PerHour; i++)
        {
            Assert.True(limiter.TryAcquire(IPAddress.Parse(address)));
        }
    }

    [Fact]
    public void StopsAddressAfterLimit()
    {
        using var limiter = new ResetIpLimiter();
        Exhaust(limiter, "203.0.113.7");

        Assert.False(limiter.TryAcquire(IPAddress.Parse("203.0.113.7")));
        Assert.True(limiter.TryAcquire(IPAddress.Parse("203.0.113.8")));
    }

    /// <summary>Смена младших 64 бит адреса IPv6 лимит не обходит</summary>
    [Fact]
    public void CountsIpv6ByPrefix64()
    {
        using var limiter = new ResetIpLimiter();
        Exhaust(limiter, "2001:db8:1:2::1");

        Assert.False(limiter.TryAcquire(IPAddress.Parse("2001:db8:1:2:ffff::5")));
        Assert.True(limiter.TryAcquire(IPAddress.Parse("2001:db8:1:3::1")));
    }

    /// <summary>IPv4, пришедший как IPv6, — тот же адрес IPv4, а не префикс /64 на всех</summary>
    [Fact]
    public void CountsMappedIpv4AsIpv4()
    {
        using var limiter = new ResetIpLimiter();
        Exhaust(limiter, "::ffff:203.0.113.7");

        Assert.False(limiter.TryAcquire(IPAddress.Parse("203.0.113.7")));
        Assert.True(limiter.TryAcquire(IPAddress.Parse("::ffff:203.0.113.8")));
    }
}
