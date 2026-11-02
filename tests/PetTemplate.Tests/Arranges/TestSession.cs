using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Db.Entity;

namespace PetTemplate.Tests.Arranges;

/// <summary>
/// Клиент с токеном продукта, выпущенным «в прошлом», — без входа по паролю.
///
/// Нужен там, где вход паролем не годится: у аккаунта пароля нет (пришёл из Telegram,
/// спец. accounts, раздел 3) или проверяется время выпуска токена (<c>iat</c>) против
/// отметки отзыва (8.1). Подписан тем же ключом, что и настоящие токены, — схема продукта
/// его принимает
/// </summary>
public static class TestSession
{
    public static async Task<(HttpClient client, ApplicationContext db)> IssuedAt(
        Guid userId,
        DateTime issuedAtUtc,
        Action<ApplicationContext>? dbArrange = null,
        SessionLogin? via = null)
    {
        var (client, services) = await new ApiApplicationFactory<Program>()
            .SetupApplication(
                dbArrange: dbArrange,
                dbName: Guid.NewGuid().ToString(),
                configureSettings: TestSettings.ConfigureAuth());

        var jwt = services.GetRequiredService<IOptions<JwtOptions>>().Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(jwt, userId, issuedAtUtc, via));

        return (client, services.GetRequiredService<ApplicationContext>());
    }

    /// <summary>
    /// Сессия, открытая входом <paramref name="via"/> — паролем или через площадку (спец. accounts,
    /// 8.3 и 8.6): токен несёт <c>auth_time</c> и <c>amr</c>, как после <c>Auth/TelegramLogin</c>
    /// или <c>Auth/LogIn</c>
    /// </summary>
    public static Task<(HttpClient client, ApplicationContext db)> SignedInVia(
        Guid userId,
        SessionLogin via,
        Action<ApplicationContext>? dbArrange = null) =>
        IssuedAt(userId, DateTime.UtcNow, dbArrange, via);

    private static string Token(JwtOptions jwt, Guid userId, DateTime issuedAtUtc, SessionLogin? via)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };
        if (via != null) claims.AddRange(FreshLogin.Claims(via));

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
