using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Двери в админку, которые открываются без админского токена (спец. admin, 3.3, 3.4
/// и 6): вход, страница приглашения и его принятие, подзапрос шлюза к Grafana, — и «кто я»
/// с выходом для оболочки.
///
/// Отказы здесь — голый <c>404</c>, без кода и текста: неверный пароль, пользователь
/// без ролей, исчерпанный лимит и битое тело отвечают одинаково и неотличимо
/// от несуществующего пути. Поэтому и <c>[ValidateRequest]</c> на входе нет —
/// его <c>422</c> сообщил бы, что дверь тут есть (3.5)
/// </summary>
[Route("api/Admin")]
[OutsideWorkspace]
public class AdminAuthController(
    AdminAccessService access,
    AdminInvitationService invitations,
    AdminTokenFactory tokens,
    AdminLoginLimiter limiter,
    IOptions<AdminOptions> options)
    : ControllerBase
{
    /// <summary>
    /// Логин и пароль — от продукта, плюс хотя бы одна роль. Вне <c>[AuthorizeAdmin]</c>:
    /// ролей запрос ещё не предъявил. Лимит считает только неудачи — и по учётке,
    /// чей это логин, и по адресу
    /// </summary>
    [HttpPost("Login")]
    [Produces(typeof(AdminLoginResultDto))]
    public async Task<IActionResult> Login([FromBody] LoginDto? dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto?.Login) || string.IsNullOrEmpty(dto.Password))
        {
            return NotFound();
        }

        var attempt = limiter.TryBegin(await access.OwnerOf(dto, ct), Address());
        if (attempt == null)
        {
            return NotFound();
        }

        var admin = await access.LogIn(dto, ct);
        if (admin == null)
        {
            return NotFound();
        }

        attempt.Succeeded();
        return SignedIn(tokens.Build(admin));
    }

    /// <summary>
    /// Страница приглашения до входа (3.4): кто позвал и какие роли выдаются. Вне
    /// <c>[AuthorizeAdmin]</c> — у пришедшего по ссылке ни ролей, ни токена. Незнакомый
    /// токен — <c>404</c> и неудача в счёт адреса: перебирать токены так же дорого,
    /// как пароли
    /// </summary>
    [HttpGet("Invitations/{token:guid}")]
    [Produces(typeof(AdminInvitationPreviewDto))]
    public async Task<IActionResult> PreviewInvitation(Guid token, CancellationToken ct)
    {
        var attempt = limiter.TryBeginAddress(Address());
        if (attempt == null)
        {
            return NotFound();
        }

        var preview = await invitations.Preview(token, ct);
        if (preview == null)
        {
            return NotFound();
        }

        attempt.Succeeded();
        return Ok(preview);
    }

    /// <summary>
    /// Принять приглашение (3.4): логин и пароль от продукта плюс живой токен. Вне
    /// <c>[AuthorizeAdmin]</c> — ролей ещё нет, а <c>Admin/Login</c> без них не пускает.
    /// Отвечает сразу админским токеном. Лимит — тот же, что у входа, и отказ тот же:
    /// голый <c>404</c> на всё
    /// </summary>
    [HttpPost("Invitations/{token:guid}/Accept")]
    [Produces(typeof(AdminLoginResultDto))]
    public async Task<IActionResult> AcceptInvitation(Guid token, [FromBody] LoginDto? dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto?.Login) || string.IsNullOrEmpty(dto.Password))
        {
            return NotFound();
        }

        var attempt = limiter.TryBegin(await access.OwnerOf(dto, ct), Address());
        if (attempt == null)
        {
            return NotFound();
        }

        var admin = await invitations.Accept(token, dto, ct);
        if (admin == null)
        {
            return NotFound();
        }

        attempt.Succeeded();
        return SignedIn(tokens.Build(admin));
    }

    /// <summary>
    /// Кто вошёл и с какими ролями — по токену, урезанному до ролей в базе (3.3): вкладки
    /// оболочки должны совпадать с тем, что сервер пустит
    /// </summary>
    [HttpGet("Me")]
    [AuthorizeAdmin]
    [Produces(typeof(AdminDto))]
    public IActionResult Me() => Ok(new AdminDto
    {
        UserId = User.GetUserId(),
        UserName = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
        Roles = User.GetAdminRoles()
    });

    /// <summary>
    /// Выход (3.3): гасит куку шлюза — клиентским кодом её, <c>HttpOnly</c>, не стереть,
    /// а без этого выход закрывал бы одну половину доступа из двух. Токен из хранилища
    /// клиент убирает сам. Под <c>[AuthorizeAdmin]</c>: заголовок чужой сайт не подставит,
    /// и разлогинить админа подложенной формой нельзя
    /// </summary>
    [HttpPost("Logout")]
    [AuthorizeAdmin]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(options.Value.GatewayCookieName, GatewayCookie(null));
        return Ok();
    }

    /// <summary>
    /// Подзапрос nginx перед Grafana (3.3): пускать ли. Вне <c>[AuthorizeAdmin]</c> —
    /// авторизуется кукой шлюза, а не заголовком. Отказ здесь <c>401</c>, а не <c>404</c>:
    /// отвечает он nginx, а не браузеру.
    ///
    /// Логин — идентификатор пользователя, а не ник: ник меняется и бывает не латиницей,
    /// а заголовок ответа — только ASCII. Роли — именами через запятую, из токена,
    /// урезанного до ролей в базе, как у <c>Me</c>
    /// </summary>
    [HttpGet("Auth/Verify")]
    public async Task<IActionResult> Verify()
    {
        var result = await HttpContext.AuthenticateAsync(AdminAuth.GatewayScheme);
        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        var roles = result.Principal.GetAdminRoles();
        if (roles.Length == 0)
        {
            return Unauthorized();
        }

        Response.Headers["X-Admin-Login"] = result.Principal.GetUserId().ToString();
        Response.Headers["X-Admin-Roles"] = string.Join(',', roles);
        return Ok();
    }

    /// <summary>
    /// Ответ входа и рядом кука для шлюза (3.3) — тот же токен и тот же срок. Атрибуты
    /// названы явно: кука на весь проект одна, и ездит она на родительский домен
    /// </summary>
    private OkObjectResult SignedIn(AdminLoginResultDto result)
    {
        Response.Cookies.Append(options.Value.GatewayCookieName, result.Token, GatewayCookie(result.ExpiresUtc));
        return Ok(result);
    }

    /// <summary>
    /// Атрибуты куки шлюза. Выход стирает её с теми же доменом и путём, что ставил
    /// вход: иначе браузер счёл бы это другой кукой и оставил прежнюю
    /// </summary>
    private CookieOptions GatewayCookie(DateTimeOffset? expires)
    {
        var domain = options.Value.GatewayCookieDomain;
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Domain = string.IsNullOrEmpty(domain) ? null : domain,
            Path = "/",
            Expires = expires
        };
    }

    /// <summary>
    /// Адрес клиента для лимита попыток. За nginx настоящий адрес подставляет
    /// <c>UseForwardedHeaders</c> — без него все входы считались бы одним адресом шлюза
    /// </summary>
    private string Address() => ClientAddress.Key(HttpContext.Connection.RemoteIpAddress);
}
