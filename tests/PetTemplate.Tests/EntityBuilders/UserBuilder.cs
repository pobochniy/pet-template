using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Tests.EntityBuilders;

public class UserBuilder
{
    private static long TestUserTelegramId = 12345678;
    
    public static readonly string SuperAdminTelegramAuth = "!Qwerty23";
    public static readonly string SuperAdminName = "admin";
    
    private readonly Profile _profile;
    private readonly List<PermissionEnum> _permissions = [];

    public UserBuilder(string name = "Oleg", Guid? id = null, long? telegramId = null)
    {
        id ??= Guid.CreateVersion7();
        telegramId ??= TestUserTelegramId;

        _profile = new Profile
        {
            Id = id.Value,
            UserName = name,
            User = new User
            {
                Id = id.Value,
                TelegramId = telegramId.Value,
                SecurityStamp = "90261657-e9f1-4b73-96f8-430c18625150",
                PasswordHash = "92EA49A646E44C805568A15AB386888AC4E1C9A3FD82159DB59A98EAF9895DFD"
            }
        };
    }

    public UserBuilder WithAllPermissions()
    {
        var allPermissions = Enum.GetValues(typeof(PermissionEnum))
            .Cast<PermissionEnum>()
            .Select(x => new UserInPermission
            {
                UserId = _profile.Id,
                PermissionId = x
            })
            .Where(x => x.PermissionId != 0)
            .ToArray();

        _profile.User.UserPermissions = allPermissions;
        _permissions.AddRange(allPermissions.Select(x => x.PermissionId));
        return this;
    }

    public UserBuilder WithPermissions(Guid organizationId, ICollection<PermissionEnum> permissions)
    {
        foreach (var permission in permissions)
        {
            _profile.User.UserPermissions.Add(new UserInPermission 
            { 
                UserId = _profile.Id, 
                PermissionId = permission
            });
            _permissions.Add(permission);
        }

        return this;
    }
    
    public UserBuilder WithPermissions(Guid organizationId, params PermissionEnum[] permissions)
    {
        return WithPermissions(organizationId, permissions.ToList());
    }

    public (string token, Profile profile) Please()
    {
        var jwtToken = GenerateJwtToken();
        return (jwtToken, _profile);
    }
    
    private string GenerateJwtToken()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _profile.Id.ToString()),
        };
        claims.AddRange(_permissions.Select(permission => new Claim(ClaimTypes.Role, ((int)permission).ToString())));

        var keyBytes = Encoding.UTF8.GetBytes("TestSigningKeyThatIsLongEnough123456");
        var symmetricKey = new SymmetricSecurityKey(keyBytes);

        var signingCredentials = new SigningCredentials(
            symmetricKey,
            SecurityAlgorithms.HmacSha256);
    
        var token = new JwtSecurityToken(
            issuer: "TestIssuer",
            audience: "TestAudience",
            claims: claims,
            expires: DateTime.Now.AddSeconds(3600),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}