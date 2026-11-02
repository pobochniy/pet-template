using System.Security.Claims;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Вход через площадку для токена (спец. accounts, 8.3): какой и когда. <see cref="At"/> —
/// время, когда площадка подтвердила человека
/// </summary>
public record ExternalLogin(ExternalProvider Provider, DateTimeOffset At);

/// <summary>
/// Чем и когда человек доказал владение аккаунтом (спец. accounts, 8.6): паролем
/// (<see cref="FreshLogin.PasswordMethod"/>) или площадкой (<see cref="FreshLogin.Method"/>)
/// </summary>
public record SessionLogin(string Method, DateTimeOffset At)
{
    public static SessionLogin Password() => new(FreshLogin.PasswordMethod, DateTimeOffset.UtcNow);

    public static implicit operator SessionLogin(ExternalLogin login) => new(FreshLogin.Method(login.Provider), login.At);
}

/// <summary>
/// Свежий вход (спец. accounts, 8.3 и 8.6): токен, выданный входом паролем или через
/// Telegram и VK, несёт время и способ входа — <c>auth_time</c> и <c>amr</c>. Окно считается
/// от времени входа, а не от выдачи токена: токен перевыпускают и смена логина, и создание
/// пространства, и окно не должно от этого продлеваться. В окне
/// <c>AccountOptions.FreshLoginWindow</c> открываются привязка, отвязка, смена почты
/// и слияние (8.6), а свежий вход площадкой ещё и задаёт пароль без старого (8.3)
/// </summary>
public static class FreshLogin
{
    public const string AuthTimeClaim = "auth_time";
    public const string MethodClaim = "amr";

    /// <summary>Значение <c>amr</c> для пароля — как в RFC 8176</summary>
    public const string PasswordMethod = "pwd";

    public static string Method(ExternalProvider provider) =>
        provider == ExternalProvider.Telegram ? "telegram" : "vk";

    /// <summary>
    /// Площадка, которой открыта сессия, — если не раньше, чем <paramref name="window"/> назад.
    /// Иначе <c>null</c>; вход паролем — тоже <c>null</c>
    /// </summary>
    public static ExternalProvider? FreshProvider(ClaimsPrincipal user, TimeSpan window) =>
        Fresh(user, window)?.Method switch
        {
            "telegram" => ExternalProvider.Telegram,
            "vk" => ExternalProvider.Vk,
            _ => null
        };

    /// <summary>Вход, которым открыта сессия, — если не раньше, чем <paramref name="window"/> назад. Иначе <c>null</c></summary>
    public static SessionLogin? Fresh(ClaimsPrincipal user, TimeSpan window) =>
        Current(user) is { } login && DateTimeOffset.UtcNow - login.At <= window ? login : null;

    /// <summary>
    /// Вход, которым открыта сессия, сколько бы времени ни прошло, — чтобы перевыпуск
    /// токена его не терял. Токен без отметки входа — <c>null</c>
    /// </summary>
    public static SessionLogin? Current(ClaimsPrincipal user)
    {
        var method = Find(user, MethodClaim, "http://schemas.microsoft.com/claims/authnmethodsreferences");
        if (method is not ("telegram" or "vk" or PasswordMethod)) return null;
        if (!long.TryParse(Find(user, AuthTimeClaim, ClaimTypes.AuthenticationInstant), out var seconds)) return null;

        return new SessionLogin(method, DateTimeOffset.FromUnixTimeSeconds(seconds));
    }

    /// <summary>
    /// Клеймы для токена: время входа («из будущего» — не позже сейчас) и способ
    /// </summary>
    public static IEnumerable<Claim> Claims(SessionLogin login)
    {
        var now = DateTimeOffset.UtcNow;
        var authTime = login.At < now ? login.At : now;
        yield return new Claim(AuthTimeClaim, authTime.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64);
        yield return new Claim(MethodClaim, login.Method);
    }

    /// <summary>
    /// Схема JWT по умолчанию переименовывает входящие <c>amr</c> и <c>auth_time</c>
    /// в длинные имена .NET — ищем под обоими
    /// </summary>
    private static string? Find(ClaimsPrincipal user, string shortName, string mappedName) =>
        (user.FindFirst(shortName) ?? user.FindFirst(mappedName))?.Value;
}
