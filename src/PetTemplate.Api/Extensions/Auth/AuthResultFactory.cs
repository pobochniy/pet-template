using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PetTemplate.Api.Middlewares;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Собирает ответ аутентификации: JWT плюс профиль и права по пространствам
/// (спец. authorization, 2.1). Прав в токене нет: сервер берёт их из базы на каждом
/// запросе (<c>SessionGuard</c>, 2.3), а клиенту они едут полем ответа. Живёт в слое API, потому что подписывающий ключ —
/// настройка хоста (<see cref="JwtOptions"/>), а не бизнес-логика.
///
/// Единственное место сборки сессии: пускать пользователя внутрь умеет не только вход —
/// создание пространства, выход из него и приглашение перевыпускают сессию ровно
/// тем же кодом (спец. authorization, 2.2), а с ними смена логина и пароля
/// (<c>AccountController</c>).
///
/// В токене — время выпуска (<see cref="IssuedAt"/>): по нему <c>SessionGuard</c> отбивает
/// токены, выданные до смены пароля (спец. accounts, 8.1). Отметку последнего входа
/// ставит не фабрика, а сам вход (<c>AuthService.LogIn</c>): перевыпуск сессии
/// после создания пространства входом не является
/// </summary>
public class AuthResultFactory(IOptions<JwtOptions> jwtOptions)
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    /// <param name="user">Кого впускаем</param>
    /// <param name="via">
    /// Вход паролем или через площадку — токен несёт время и способ входа (<see cref="FreshLogin"/>,
    /// спец. accounts, 8.3 и 8.6); <c>null</c> — приглашение, сброс, перевыпуск
    /// </param>
    public AuthenticationResultDto Build(UserDto user, SessionLogin? via = null) => new()
    {
        Token = CreateToken(user, via),
        Profile = user.Profile,
        PermissionsByWorkspace = user.PermissionsByWorkspace,
        IsNewAccount = user.IsNewAccount
    };

    private string CreateToken(UserDto user, SessionLogin? via)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            // Время выпуска — по нему отбиваются токены, выданные до смены пароля
            // (спец. accounts, 8.1; `SessionGuard`)
            IssuedAt.Now()
        };

        if (via != null)
        {
            claims.AddRange(FreshLogin.Claims(via));
        }

        var keyBytes = Encoding.UTF8.GetBytes(_jwtOptions.SigningKey);
        var symmetricKey = new SymmetricSecurityKey(keyBytes);

        var signingCredentials = new SigningCredentials(
            symmetricKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(_jwtOptions.ExpirationSeconds),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
