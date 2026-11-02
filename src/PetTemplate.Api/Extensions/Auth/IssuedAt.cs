using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Время выпуска токена — клейм <c>iat</c> в секундах Unix (спец. accounts, 8.1).
/// По нему <c>SessionGuard</c> отбивает токены, выпущенные до отзыва. Одно место
/// на запись и чтение — у токена продукта и у админского (<c>AdminTokenFactory</c>)
/// </summary>
public static class IssuedAt
{
    /// <summary>Клейм «выпущен сейчас»</summary>
    public static Claim Now() => At(DateTime.UtcNow);

    /// <summary>
    /// Клейм «выпущен в <paramref name="utc"/>» — когда рядом из того же момента считается
    /// и срок токена: одна отметка времени на оба
    /// </summary>
    public static Claim At(DateTime utc) =>
        new(JwtRegisteredClaimNames.Iat,
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(),
            ClaimValueTypes.Integer64);

    /// <summary>Время выпуска из токена; клейма нет — <c>null</c></summary>
    public static DateTime? Of(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
}
