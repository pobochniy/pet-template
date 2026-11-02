using System.Net;
using System.Net.Sockets;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Ключ адреса клиента для лимитов попыток (<see cref="ResetIpLimiter"/>,
/// <see cref="AdminLoginLimiter"/>). IPv6 — по префиксу /64: столько обычно выдают одному
/// абоненту, и счёт по полному адресу перебор обошёл бы сменой младших битов.
/// IPv4, отображённый в IPv6, считается как IPv4. Адреса нет — общий ключ: такие запросы
/// делят один лимит.
///
/// Адрес — из <c>X-Forwarded-For</c> (<c>ConfigureForwardedHeaders</c>), иначе у всех был
/// бы один — адрес nginx
/// </summary>
public static class ClientAddress
{
    private const int Ipv6PrefixBytes = 8;

    public static string Key(IPAddress? address)
    {
        if (address == null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6PrefixBytes, bytes.Length - Ipv6PrefixBytes);
        return new IPAddress(bytes) + "/64";
    }
}
