using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using PetTemplate.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PetTemplate.Api.Middlewares;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Api.Controllers;

[Route("api/[controller]/[action]")]
[AllowAnonymous]
public class AuthController(AuthService service, 
    ILogger<AuthController> logger,
    TelegramService telegramService, 
    IOptions<JwtOptions> jwtOptions,
    IWebHostEnvironment environment)
    : ControllerBase
{

    private readonly JwtOptions _jwtOptions = jwtOptions.Value;
    
    private string CreateToken(UserDto user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        };
        claims.AddRange(user.Permissions.Select(role => new Claim(ClaimTypes.Role, ((int)role).ToString())));

        var keyBytes = Encoding.UTF8.GetBytes(_jwtOptions.SigningKey);
        var symmetricKey = new SymmetricSecurityKey(keyBytes);

        var signingCredentials = new SigningCredentials(
            symmetricKey,
            SecurityAlgorithms.HmacSha256);
    
        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.Now.AddSeconds(_jwtOptions.ExpirationSeconds),
            signingCredentials: signingCredentials);

        var rawToken = new JwtSecurityTokenHandler().WriteToken(token);
        return rawToken;
    }
    
    [HttpPost]
    public async Task<IActionResult> TelegramAuth([FromBody] TelegramAuthDto query)
    {
        // В Development режиме пропускаем валидацию если hash = "mock_hash_for_development"
        var isDevelopment = environment.IsDevelopment();
        
        if (!isDevelopment)
        {
            var isValid = telegramService.ValidateInitData(query.InitData);
            if (!isValid)
            {
                logger.LogWarning(query.InitData);
                throw new ArgumentException("Invalid Telegram signature");
            }
        }

        var telegramUser = telegramService.ExtractTelegramUser(query.InitData);
        if (telegramUser == null)
            throw new ArgumentException("Cannot extract Telegram user data");

        var displayName = telegramUser.Username 
                          ?? telegramUser.FirstName 
                          ?? $"user_{telegramUser.Id}";
        
        var user = await service.GetOrRegisterUser(telegramUser.Id, displayName);
        var token = CreateToken(user);

        return Ok(new AuthenticationResultDto
        {
            Token = token,
            Profile = user.Profile,
            Permissions = user.Permissions
        });
    }
    
    [HttpGet]
#if DEBUG
    [ApiExplorerSettings(IgnoreApi = false)]
#else
    [ApiExplorerSettings(IgnoreApi = true)]
#endif
    public IActionResult GenerateTestInitData(string username = "Admin", long userId = 123456789)
    {
#if !DEBUG
        return NotFound();
#else
        var initData = telegramService.GenerateTestInitData(username, userId);
        return Ok(new { initData });
#endif
    }
}