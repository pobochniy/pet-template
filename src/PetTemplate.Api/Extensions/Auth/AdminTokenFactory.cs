using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Токен админской схемы (спец. admin, 3.3): своя аудитория, свой ключ подписи,
/// роли клеймами и час жизни без продления. Рядом с <see cref="AuthResultFactory"/>
/// по той же причине — ключ подписи это настройка хоста
/// </summary>
public class AdminTokenFactory(IOptions<AdminOptions> options)
{
    private readonly AdminOptions _options = options.Value;

    public AdminLoginResultDto Build(AdminDto admin)
    {
        // Одна отметка на время выпуска и срок: взятые порознь, они могли бы
        // разойтись на секунду
        var issued = DateTime.UtcNow;
        var expires = issued.Add(_options.TokenLifetime);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.UserId.ToString()),
            new(ClaimTypes.Name, admin.UserName),
            // Отзыв сессий — тот же, что у продукта (спец. accounts, 8.1 и 10): в админку входят
            // паролем продукта, и его смена, как и удаление аккаунта, гасит и этот токен
            IssuedAt.At(issued)
        };
        claims.AddRange(admin.Roles.Select(x => new Claim(CustomClaimTypes.AdminRole, ((int)x).ToString())));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new AdminLoginResultDto
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresUtc = expires,
            Admin = admin
        };
    }
}
