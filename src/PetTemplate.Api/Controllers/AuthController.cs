using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Web;
using LA.Atheneum.Dto.Auth;
using PetTemplate.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Interfaces;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Api.Controllers;

[Route("api/[controller]/[action]")]
[AllowAnonymous]
[ValidateRequest]
public class AuthController(AuthService service, 
    IVkService vkService,
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

    private AuthenticationResultDto BuildResult(UserDto user) => new()
    {
        Token = CreateToken(user),
        Profile = user.Profile,
        Permissions = user.Permissions
    };
    
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
        return Ok(BuildResult(user));
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
    
    [HttpPost]
    public async Task<IActionResult> VkAuth([FromBody] VkAuthDto query)
    {
        var isRequestValid = vkService.IsSignValid(ToQueryStringUsingReflection(query));
        if (!isRequestValid)
        {
            // await logs.Add(Request.QueryString.ToString(), vkId: query.vk_user_id);
            return BadRequest("Sign is invalid");
        }

        var user = await service.GetOrRegisterUserForVk(query.vk_user_id, null);
        return Ok(BuildResult(user));
        // return Redirect("http://localhost:4200");
        // return Redirect("https://wisdomsnakes.ru");
    }
    
    [HttpPost]
    public async Task<IActionResult> MiniAuth([FromBody] VkAuthDto query)
    {
        var isRequestValid = vkService.IsSignValid(ToQueryStringUsingReflection(query));
        if (!isRequestValid)
        {
            // await logs.Add(Request.QueryString.ToString(), vkId: query.vk_user_id);
            throw new ArgumentException("Sign is invalid");
        }

        var user = await service.GetOrRegisterUserForVk(query.vk_user_id, null);
        return Ok(BuildResult(user));
    }
    
    private static string ToQueryStringUsingReflection<T>(T obj)
    {
        var properties = from p in obj?
                .GetType()
                .GetProperties()
            where p.GetValue(obj, null) != null
            select $"{HttpUtility.UrlEncode(p.Name)}" +
                   $"={HttpUtility.UrlEncode(p.GetValue(obj)?.ToString())}";
        return string.Join("&", properties);
    }
    
    // [HttpGet]
    // public async Task<IActionResult> VkOAuthCallback([FromQuery] string code)
    // {
    //     try
    //     {
    //         // await logs.Add($"code = [{code}]");
    //         var token = await GetVkUserToken(code);
    //         // await logs.Add($"token = [{token}]");
    //
    //         var user = await service.GetOrRegisterUserForVk(token.user_id, token.access_token);
    //         await SignInAsync(user, token.user_id);
    //
    //         // return Redirect("http://localhost:4200");
    //         // return Redirect("https://wisdomsnakes.ru");
    //         var html = await GetWisdomIndex();
    //         return Content(html, "text/html", Encoding.UTF8);
    //     }
    //     catch (Exception e)
    //     {
    //         // if(e.InnerException != null) await logs.Add($"e.InnerException = [{e.InnerException}]");
    //         // await logs.Add(e.Message);
    //         // await logs.Add(e.StackTrace);
    //         return BadRequest(e.Message);
    //     }
    //     
    // }

    // private async Task<string> GetWisdomIndex()
    // {
    //     var response = await SharedClient.GetAsync(config.GetValue<string>("Domain")); //"https://wisdomsnakes.ru"
    //     response.EnsureSuccessStatusCode();
    //     return await response.Content.ReadAsStringAsync();
    // }
    
    // private async Task<VkTokenDto> GetVkUserToken(string code)
    // {
    //     var appId = 51814637;
    //     var redirectUrl = "https://wisdomsnakes.ru/api/Auth/VkOAuthCallback";
    //     var secretKey = "2tTLvms691YgWrHluboW";
    //     var response = await SharedClient.GetAsync($"https://oauth.vk.com/access_token?client_id={appId}&client_secret={secretKey}&redirect_uri={redirectUrl}&code={code}");
    //     // await logs.Add(await response.Content.ReadAsStringAsync());
    //     response.EnsureSuccessStatusCode();
    //     return await response.Content.ReadFromJsonAsync<VkTokenDto>();
    // }

    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> Register([FromBody] RegisterDto model, CancellationToken ct)
    {
        try
        {
            await service.Register(model, ct);
            var user = await service.LogIn(new LoginDto { Login = model.UserName, Password = model.Password }, ct);
            return Ok(BuildResult(user));
        }
        catch (Exception e)
        {
            return BadRequest($"{e.GetType()}: {e.Message}");
        }
    }

    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> LogIn([FromBody] LoginDto model, CancellationToken ct)
    {
        try
        {
            var user = await service.LogIn(model, ct);
            return Ok(BuildResult(user));
        }
        catch (UnauthorizedAccessException)
        {
            ModelState.AddModelError("", "Неправильное имя пользователя или пароль");
            return UnprocessableEntity(ModelState);
        }
        catch (Exception e)
        {
            return BadRequest($"{e.GetType()}: {e.Message}");
        }
    }
}