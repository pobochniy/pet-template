using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Tests.EntityBuilders;

public class UserBuilder
{
    private static long _lastTelegramId = 12345678;
    
    /// <summary>Пароль аккаунта из билдера — от него хеш прежней схемы в конструкторе</summary>
    public static readonly string SuperAdminPassword = "!Qwerty23";

    public static readonly string SuperAdminTelegramAuth = SuperAdminPassword;
    public static readonly string SuperAdminName = "admin";
    
    private readonly Profile _profile;
    private readonly Dictionary<Guid, List<PermissionEnum>> _workspacePermissions = new();

    public UserBuilder(string name = "Oleg", Guid? id = null, long? telegramId = null)
    {
        id ??= Guid.CreateVersion7();
        // Телеграм-id уникален в базе (`ix_users_telegram_id`): двум пользователям одного
        // теста нужен каждому свой. `Interlocked` — потому что xunit гоняет классы параллельно
        telegramId ??= Interlocked.Increment(ref _lastTelegramId);

        _profile = new Profile
        {
            Id = id.Value,
            UserName = name,
            // Хеш прежней схемы (SHA-256 от «соль#пароль») от SuperAdminPassword: первый вход
            // пересчитывает его на PBKDF2 (спец. accounts, 8.1) — так каждый тест со входом
            // заодно проверяет, что старые пароли входят
            User = new User
            {
                Id = id.Value,
                CreatedAtUtc = DateTime.UtcNow,
                TelegramId = telegramId.Value,
                SecurityStamp = "90261657-e9f1-4b73-96f8-430c18625150",
                PasswordHash = "92EA49A646E44C805568A15AB386888AC4E1C9A3FD82159DB59A98EAF9895DFD"
            }
        };
    }

    /// <summary>
    /// Почта и телефон — чтобы проверить, что чужим они не уезжают (спец. authorization, 5)
    /// </summary>
    public UserBuilder WithContacts(string email, string phone)
    {
        _profile.Email = email;
        _profile.PhoneNumber = phone;
        return this;
    }

    /// <summary>
    /// Аккаунт без пароля — как пришедший из Telegram или VK (спец. accounts, раздел 3):
    /// паролем не войти, клиент — с готовым токеном (<c>TestSession</c>)
    /// </summary>
    public UserBuilder WithoutPassword()
    {
        _profile.User!.PasswordHash = null;
        _profile.User.SecurityStamp = null;
        return this;
    }

    /// <summary>
    /// Без Telegram: способом войти остаётся только пароль (спец. accounts, раздел 3).
    /// По умолчанию билдер выдаёт каждому свой <c>TelegramId</c> — ради уникального индекса
    /// </summary>
    public UserBuilder WithoutTelegram()
    {
        _profile.User!.TelegramId = null;
        return this;
    }

    /// <summary>Привязанный Telegram с этим id (спец. accounts, раздел 3)</summary>
    public UserBuilder WithTelegram(long telegramId)
    {
        _profile.User!.TelegramId = telegramId;
        return this;
    }

    /// <summary>Привязанный VK (спец. accounts, раздел 3)</summary>
    public UserBuilder WithVk(long vkId)
    {
        _profile.User!.VkId = vkId;
        return this;
    }

    /// <summary>
    /// Основное пространство (спец. authorization, 7.1)
    /// </summary>
    public UserBuilder WithPrimaryWorkspace(Guid workspaceId)
    {
        _profile.PrimaryWorkspaceId = workspaceId;
        return this;
    }

    /// <summary>
    /// Все права в пространстве — набор владельца. Само пространство тест кладёт в базу
    /// сам (<see cref="WorkspaceBuilder"/>): права ссылаются на него внешним ключом
    /// </summary>
    public UserBuilder WithAllPermissions(Guid workspaceId)
    {
        var allPermissions = Enum.GetValues<PermissionEnum>()
            .Where(x => x != PermissionEnum.None)
            .ToList();

        return WithPermissions(workspaceId, allPermissions);
    }

    /// <summary>
    /// Права в пространстве — и строками в базе, и клеймами в токене
    /// (<c>ws_permission = {workspaceId}|{(int)permission}</c>), как их выдаёт вход
    /// </summary>
    public UserBuilder WithPermissions(Guid workspaceId, ICollection<PermissionEnum> permissions)
    {
        if (!_workspacePermissions.TryGetValue(workspaceId, out var granted))
        {
            granted = [];
            _workspacePermissions[workspaceId] = granted;
        }

        foreach (var permission in permissions)
        {
            _profile.User!.UserPermissions.Add(new UserInPermission
            {
                WorkspaceId = workspaceId,
                UserId = _profile.Id,
                PermissionId = permission
            });
            granted.Add(permission);
        }

        return this;
    }

    public UserBuilder WithPermissions(Guid workspaceId, params PermissionEnum[] permissions)
    {
        return WithPermissions(workspaceId, permissions.ToList());
    }

    /// <summary>
    /// Роли админки (спец. admin, 3.1) — строками в базе, по одной на роль, как их заводит
    /// владелец. В токен продукта они не едут: админский токен выдаёт <c>Admin/Login</c>
    /// </summary>
    public UserBuilder WithAdminRoles(params AdminRole[] roles)
    {
        foreach (var role in roles)
        {
            _profile.User!.AdminRoles.Add(new UserInAdminRole
            {
                UserId = _profile.Id,
                Role = role,
                GrantedUtc = DateTime.UtcNow
            });
        }

        return this;
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
        // Как после входа паролем (спец. accounts, 8.6): окно свежего входа открыто, и привязка,
        // отвязка и смена почты проходят без подтверждения. Давняя сессия без отметки —
        // `TestSession.IssuedAt`
        claims.AddRange(FreshLogin.Claims(SessionLogin.Password()));

        foreach (var workspace in _workspacePermissions)
        {
            foreach (var permission in workspace.Value)
            {
                claims.Add(new Claim(CustomClaimTypes.WorkspacePermission, $"{workspace.Key}|{(int)permission}"));
            }
        }

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