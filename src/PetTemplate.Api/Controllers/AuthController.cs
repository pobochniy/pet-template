using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Controllers;

[Route("api/[controller]/[action]")]
[AllowAnonymous]
[ValidateRequest]
public class AuthController(
    AuthService service,
    AccountService account,
    AccountMergeService merge,
    AuthResultFactory authResult,
    PasswordReset passwordReset,
    EmailConfirmation emails,
    ResetIpLimiter resetLimiter,
    LoginLimiter loginLimiter,
    MiniAppLaunch launch,
    ExternalProof externalProof,
    TelegramLogin telegramLogin,
    VkId vkId,
    WorkspaceInvitationService invitations,
    IOptions<TelegramLoginOptions> telegramOptions,
    IOptions<VkIdOptions> vkOptions)
    : ControllerBase
{
    /// <summary>
    /// Публичные параметры входа через площадки (спец. accounts, 5.1): клиент собирает
    /// по ним адрес авторизации. Пустой id — площадка выключена, кнопки нет
    /// </summary>
    [HttpGet]
    [Produces(typeof(ExternalLoginsConfigDto))]
    public IActionResult ExternalLogins()
    {
        var telegram = telegramOptions.Value;
        var vk = vkOptions.Value;
        return Ok(new ExternalLoginsConfigDto
        {
            TelegramBotId = telegram.BotId,
            TelegramAuthorizationEndpoint = telegram.AuthorizationEndpoint,
            TelegramRedirectUri = telegram.RedirectUri,
            VkAppId = vk.AppId,
            VkAuthorizationEndpoint = vk.AuthorizationEndpoint,
            VkRedirectUri = vk.RedirectUri
        });
    }

    /// <summary>
    /// Вход и регистрация через Telegram на сайте (5.2): в <see cref="ExternalCodeDto.Code"/> —
    /// <c>id_token</c> окна «Log In with Telegram», в <see cref="ExternalCodeDto.State"/> — его
    /// <c>nonce</c>; подпись проверяется здесь же, без запросов в Telegram. Известный входит,
    /// неизвестный заводит аккаунт с автоматическим логином (4.2). Не прошёл —
    /// <c>400 auth.error.externalFailed</c>. С <see cref="ExternalCodeDto.ExistingOnly"/>
    /// (восстановление доступа, 8.3) неизвестный — <c>404 auth.error.loginNotLinked</c>
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> TelegramLogin([FromBody] ExternalCodeDto dto, CancellationToken ct)
    {
        var identity = (string.IsNullOrEmpty(dto.Code) ? null : await telegramLogin.Resolve(dto.Code, dto.State))
                       ?? throw new BadRequestException(AuthMessages.ExternalFailed);

        return Ok(await SignIn(ExternalProvider.Telegram, identity, dto, ct));
    }

    /// <summary>
    /// Вход и регистрация через VK ID на сайте (5.3): сервер меняет код на <c>user_id</c> и
    /// входит или заводит аккаунт с логином <c>vk&lt;id&gt;</c>. VK не ответил или код не
    /// принял — <c>400 auth.error.externalFailed</c>. <see cref="ExternalCodeDto.ExistingOnly"/> —
    /// как у <see cref="TelegramLogin"/>
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> VkId([FromBody] ExternalCodeDto dto, CancellationToken ct)
    {
        var identity = (string.IsNullOrEmpty(dto.Code)
                           ? null
                           : await vkId.Exchange(dto.Code, dto.Verifier ?? string.Empty, dto.DeviceId, dto.State, ct))
                       ?? throw new BadRequestException(AuthMessages.ExternalFailed);

        return Ok(await SignIn(ExternalProvider.Vk, identity, dto, ct));
    }

    /// <summary>
    /// Общий хвост входа площадкой с сайта. Восстановление доступа (8.2, 8.3) входит только
    /// в существующий аккаунт: человек пришёл за своим, и пустой новый ему не поможет.
    /// Токен несёт время и способ входа — для окна свежего входа (8.3)
    /// </summary>
    private async Task<AuthenticationResultDto> SignIn(ExternalProvider provider, ExternalIdentity identity,
        ExternalCodeDto dto, CancellationToken ct)
    {
        var user = dto.ExistingOnly
            ? await service.FindExternal(provider, identity.Id, ct) ?? throw new NotFoundException(AuthMessages.LoginNotLinked)
            : await Register(provider, identity, dto.Language, ct);

        return authResult.Build(user, identity.Via(provider));
    }

    /// <summary>Известный вход — в свой аккаунт, неизвестный заводит аккаунт (4.2)</summary>
    private Task<UserDto> Register(ExternalProvider provider, ExternalIdentity identity, string? language,
        CancellationToken ct) =>
        provider == ExternalProvider.Telegram
            ? service.GetOrRegisterUser(identity.Id, identity.UserName, language, ct)
            // VK называет язык своего интерфейса в подписанных параметрах mini-app — профиль
            // получает его, а не язык браузера
            : service.GetOrRegisterUserForVk(identity.Id, identity.Language ?? language, ct);

    /// <summary>
    /// Вход из mini-app Telegram по подписанному <c>initData</c>. Подпись неверная или
    /// старше суток (<c>AccountOptions.LaunchParamsMaxAge</c>) — отказ (спец. accounts, 5.6).
    ///
    /// Неизвестный Telegram без приглашения — <c>404 auth.error.unknownLogin</c>, аккаунт
    /// не заводится: клиент показывает развилку, и заводит его <see cref="RegisterExternal"/>
    /// или привязывает к существующему <see cref="LogInAndLink"/>. С приглашением
    /// (<c>start_param=inv_…</c>) — заводится сразу
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> TelegramAuth([FromBody] TelegramAuthDto query, CancellationToken ct)
    {
        var telegramUser = launch.Telegram(query.InitData)
                           ?? throw new BadRequestException(AuthMessages.ExternalFailed);

        var known = await service.FindExternal(ExternalProvider.Telegram, telegramUser.Id, ct);
        if (known == null && !MiniAppLaunch.HasInvitation(query.InitData!))
        {
            throw new NotFoundException(AuthMessages.UnknownLogin);
        }

        // Логин нового аккаунта — `@username`, если подходит, иначе `tg<id>` (4.2). Имя и
        // фамилия логином не становятся: в них пробелы и кириллица, правилам 4.1 они не отвечают
        var user = known ?? await service.GetOrRegisterUser(telegramUser.Id, telegramUser.Username, query.Language, ct);
        // Время входа — время подписи площадки, а не сейчас (8.3)
        return Ok(authResult.Build(user, new ExternalLogin(ExternalProvider.Telegram, telegramUser.SignedAt)));
    }

    /// <summary>
    /// Вход из mini-app VK по подписанным параметрам запуска; подпись неверная или
    /// старше суток — отказ (5.6). Неизвестный VK — <c>404 auth.error.unknownLogin</c>, как у
    /// Telegram, — кроме открытых по приглашению: приглашение приходит из неподписанного
    /// фрагмента, поэтому развилку пропускает только живое — не отозванное, не принятое
    /// и не истёкшее. <c>Auth/MiniAuth</c>, копия этого метода, удалён
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> VkAuth([FromBody] VkAuthDto query, CancellationToken ct)
    {
        var vkUser = launch.Vk(query.LaunchParams)
                     ?? throw new BadRequestException(AuthMessages.ExternalFailed);

        var user = await service.FindExternal(ExternalProvider.Vk, vkUser.Id, ct);
        if (user == null)
        {
            if (query.Invitation is not { } token || !await invitations.IsAlive(token, ct))
            {
                throw new NotFoundException(AuthMessages.UnknownLogin);
            }

            user = await service.GetOrRegisterUserForVk(vkUser.Id, vkUser.Language ?? query.Language, ct);
        }

        return Ok(authResult.Build(user, new ExternalLogin(ExternalProvider.Vk, vkUser.SignedAt)));
    }

    /// <summary>
    /// Развилка mini-app, «Начать» (5.6): завести аккаунт по тому же подписанному входу,
    /// с автоматическим логином (4.2). Уже известный вход просто входит
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> RegisterExternal([FromBody] RegisterExternalDto dto, CancellationToken ct)
    {
        var identity = await externalProof.Resolve(dto.Proof, ct)
                       ?? throw new BadRequestException(AuthMessages.ExternalFailed);
        var provider = dto.Proof.Provider!.Value;

        var user = await Register(provider, identity, dto.Language, ct);
        return Ok(authResult.Build(user, identity.Via(provider)));
    }

    /// <summary>
    /// Развилка mini-app, «У меня уже есть аккаунт» (5.6): логин и пароль плюс тот же
    /// подписанный вход одним вызовом — войти и привязать этот Telegram (VK) к аккаунту.
    /// Неверные логин или пароль — <c>422</c>, как у <see cref="LogIn"/>.
    ///
    /// Вход уже принадлежит другому аккаунту (его завели между развилкой и этим вызовом) —
    /// привязки нет, а занятый вход ведёт в слияние (раздел 3, 7.1): ответ — сессия аккаунта
    /// по паролю и просмотр слияния с владельцем входа (<see cref="AuthenticationResultDto.MergePreview"/>).
    /// Владение текущим доказал пароль только что — это и есть свежий вход, которого просит
    /// просмотр слияния (8.6).
    ///
    /// Привязка ставит <c>User.TelegramLinkedAtUtc</c> (<c>VkLinkedAtUtc</c>): сутки этот вход
    /// не задаёт пароль без старого и не подтверждает владение (8.3). Сессия отмечена входом
    /// паролем (<c>pwd</c>, 8.6): владение доказал пароль, а не только что привязанный вход
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> LogInAndLink([FromBody] LogInAndLinkDto dto, CancellationToken ct)
    {
        var identity = await externalProof.Resolve(dto.Proof, ct)
                       ?? throw new BadRequestException(AuthMessages.ExternalFailed);

        // Вход паролем — под тем же лимитом, что Auth/LogIn (спец. accounts, 8.1)
        var login = new LoginDto { Login = dto.Login, Password = dto.Password };
        var attempt = await BeginLoginAttempt(login, ct);

        UserDto user;
        try
        {
            user = await service.LogIn(login, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return InvalidCredentials();
        }

        attempt.Succeeded();

        var provider = dto.Proof.Provider!.Value;
        var owner = await account.Link(user.Id, provider, identity.Id, ct);

        var session = authResult.Build(await service.GetUser(user.Id, ct), SessionLogin.Password());
        if (owner is { } otherId)
        {
            session.MergePreview = await merge.Preview(user.Id, otherId, FreshLogin.Method(provider), ct);
        }

        return Ok(session);
    }

    /// <summary>
    /// Переход по ссылке подтверждения почты (спец. accounts, 8.4). Без входа: письмо
    /// открывают и на устройстве, где человек не вошёл. Ссылка не живая —
    /// <c>400 auth.error.linkInvalid</c>
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ConfirmEmail([FromBody] EmailTokenDto dto, CancellationToken ct)
    {
        await emails.Confirm(dto.Token, ct);
        return Ok();
    }

    /// <summary>
    /// «Забыли пароль?» (8.4): ответ всегда один — есть такой аккаунт или нет, подтверждена
    /// ли у него почта, исчерпан ли лимит. Иначе форма отвечала бы на вопрос
    /// «зарегистрирован ли этот человек»
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RequestReset([FromBody] RequestResetDto dto, CancellationToken ct)
    {
        if (resetLimiter.TryAcquire(HttpContext.Connection.RemoteIpAddress))
        {
            await passwordReset.Request(dto.Email, ct);
        }

        return Ok();
    }

    /// <summary>
    /// Новый пароль по ссылке из письма или от поддержки (8.2): токен гаснет, остальные
    /// сессии завершаются, ответ — новая сессия, вход без повторного ввода
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> Reset([FromBody] ResetPasswordDto dto, CancellationToken ct)
    {
        var userId = await passwordReset.Reset(dto.Token, dto.NewPassword, ct);
        return Ok(authResult.Build(await service.GetUser(userId, ct)));
    }

    /// <summary>
    /// Регистрация по паролю. Занятые логин, почта и телефон — <c>400</c> с кодом
    /// <c>auth.error.*</c> (<see cref="AuthMessages"/>); правила полей — <c>422</c> у поля.
    /// <c>language</c> — язык интерфейса, на нём уйдут письма; почта в форме — сразу
    /// письмо подтверждения (спец. accounts, 8.4). Регистрация паролем — свежий вход (8.6)
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> Register([FromBody] RegisterDto model, CancellationToken ct)
    {
        // С названием пространства — аккаунт и пространство одним сохранением,
        // и ответ входа уже несёт права нового пространства (спец. authorization, 3, п. 1)
        await service.Register(model, ct);
        var user = await service.LogIn(new LoginDto { Login = model.UserName, Password = model.Password }, ct);
        return Ok(authResult.Build(user, SessionLogin.Password()));
    }

    /// <summary>
    /// Вход по логину, почте или телефону. Не вышло — <c>422</c> одной фразой на всю форму:
    /// «нет такого» и «не тот пароль» неразличимы намеренно (<see cref="AuthMessages.InvalidCredentials"/>).
    /// Неудач больше лимита (8.1) — <c>400 auth.error.tooManyAttempts</c>. Токен несёт время и способ входа — <c>pwd</c>: вход паролем открывает окно свежего входа (8.6)
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> LogIn([FromBody] LoginDto model, CancellationToken ct)
    {
        var attempt = await BeginLoginAttempt(model, ct);
        try
        {
            var user = await service.LogIn(model, ct);
            attempt.Succeeded();
            return Ok(authResult.Build(user, SessionLogin.Password()));
        }
        catch (UnauthorizedAccessException)
        {
            return InvalidCredentials();
        }
    }

    /// <summary>
    /// Вход паролем под лимитом неудач (спец. accounts, 8.1): счёт общий со входом в админку
    /// (<see cref="LoginLimiter"/>) — пароль у админа тот же, и без этого лимит админки
    /// обходился бы подбором здесь. Считает по учётке, чей это логин, и по адресу. Заперто —
    /// <c>400 auth.error.tooManyAttempts</c> и на верный пароль
    /// </summary>
    private async Task<LoginLimiter.Attempt> BeginLoginAttempt(LoginDto login, CancellationToken ct) =>
        loginLimiter.TryBeginProduct(
            await service.UserIdOf(login, ct), ClientAddress.Key(HttpContext.Connection.RemoteIpAddress))
        ?? throw new BadRequestException(AuthMessages.TooManyAttempts);

    /// <summary>
    /// Неверная пара логин–пароль: <c>422</c> одной фразой на всю форму
    /// (<see cref="ValidationFields.Payload"/>) — у входа и у развилки mini-app
    /// </summary>
    private UnprocessableEntityObjectResult InvalidCredentials() =>
        UnprocessableEntity(new Dictionary<string, ValidationError[]>
        {
            [ValidationFields.Payload] = [new ValidationError(
                AuthMessages.InvalidCredentials.Code,
                Text: AuthMessages.InvalidCredentials.Text)]
        });
}