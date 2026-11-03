using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;
using Microsoft.Extensions.Options;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Свой аккаунт: логин, пароль, контакты, входы площадок, слияние, удаление (спец. accounts, раздел 12). Личный экран —
/// <c>[Authorize]</c> без прав workspace: аккаунт у человека один на все его пространства,
/// и заголовок <c>X-Workspace-Id</c> здесь ни при чём
/// </summary>
[Route("api/[controller]/[action]")]
[Authorize]
[ValidateRequest]
public class AccountController(
    AccountService service,
    EmailConfirmation emails,
    AuthService auth,
    AuthResultFactory authResult,
    ExternalProof externalProof,
    AccountMergeService merge,
    AccountCards cards,
    LoginLimiter loginLimiter,
    IOptions<AccountOptions> options) : ControllerBase
{
    /// <summary>Блок «Основное» (раздел 6)</summary>
    [HttpGet]
    [Produces(typeof(AccountLoginsDto))]
    public async Task<IActionResult> Logins(CancellationToken ct)
    {
        return Ok(await LoginsOf(User.GetUserId(), ct));
    }

    /// <summary>
    /// Привязать Telegram или VK (раздел 6): на сайте — тем же окном Telegram или кодом VK ID,
    /// что вход, в mini-app — подписанными параметрами запуска. Доказательство не прошло —
    /// <c>400 auth.error.externalFailed</c>; другой вход этой площадки уже привязан —
    /// <c>400 auth.error.loginAlreadyLinked</c>. Вход принадлежит другому аккаунту — привязки
    /// нет, в ответе сразу просмотр слияния (7.1): код площадки одноразовый, второй раз
    /// доказать им владение не выйдет. Иначе — блок «Основное».
    /// Без свежего входа (8.6) — <c>400 auth.error.freshLoginRequired</c>, код площадки
    /// не тратится: свежесть проверяется до доказательства. Поэтому и просмотр слияния
    /// отсюда выдаётся только после свежего входа
    /// </summary>
    [HttpPost]
    [Produces(typeof(LinkResultDto))]
    public async Task<IActionResult> Link([FromBody] ExternalProofDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await RequireFreshLogin(userId, ct);

        var identity = await externalProof.Resolve(dto, ct)
                       ?? throw new BadRequestException(AuthMessages.ExternalFailed);
        var provider = dto.Provider!.Value;

        if (await service.Link(userId, provider, identity.Id, ct) is { } otherId)
        {
            return Ok(new LinkResultDto
            {
                MergePreview = await merge.Preview(userId, otherId, FreshLogin.Method(provider), ct)
            });
        }

        return Ok(new LinkResultDto { Logins = await LoginsOf(userId, ct) });
    }

    /// <summary>
    /// Отвязать Telegram или VK. Последний способ войти — <c>400 auth.error.lastLogin</c> (раздел 3),
    /// без свежего входа — <c>400 auth.error.freshLoginRequired</c> (8.6)
    /// </summary>
    [HttpPost]
    [Produces(typeof(AccountLoginsDto))]
    public async Task<IActionResult> Unlink([FromBody] UnlinkDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await RequireFreshLogin(userId, ct);
        await service.Unlink(userId, dto.Provider!.Value, ct);
        return Ok(await LoginsOf(userId, ct));
    }

    /// <summary>
    /// Почта, телефон, комментарий и язык (раздел 6) — логин не здесь, у него свой вызов.
    /// Занятые почта или телефон — <c>400</c>, писем подтверждения сверх лимита —
    /// <c>400 auth.error.tooManyEmails</c> (8.4). Почта меняется только в окне свежего входа
    /// (8.6): иначе <c>400 auth.error.freshLoginRequired</c>, и не сохраняется ничего. Ответ —
    /// блок «Основное», каким он лёг в базу
    /// </summary>
    [HttpPost]
    [Produces(typeof(AccountLoginsDto))]
    public async Task<IActionResult> Save([FromBody] AccountSaveDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (await service.EmailChanges(userId, dto.Email, ct))
        {
            await RequireFreshLogin(userId, ct);
        }

        return Ok(await WithFreshness(userId, await service.Save(userId, dto, ct), ct));
    }

    /// <summary>
    /// Язык из переключателя в шапке (8.4) — одним полем: <see cref="Save"/> принимает
    /// контакты целиком, и переключатель затёр бы их устаревшей копией
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SetLanguage([FromBody] SetLanguageDto dto, CancellationToken ct)
    {
        await service.SetLanguage(User.GetUserId(), dto.Language, ct);
        return Ok();
    }

    /// <summary>
    /// «Отправить ещё раз» письмо подтверждения (8.4) — не чаще лимита:
    /// сверх — <c>400 auth.error.tooManyEmails</c>, без почты — <c>400 auth.error.noEmail</c>
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SendEmailConfirmation(CancellationToken ct)
    {
        await emails.Resend(User.GetUserId(), ct);
        return Ok();
    }

    /// <summary>
    /// Смена логина (4.3). Занятый — <c>400 auth.error.userNameTaken</c>. Ответ — новая
    /// сессия без отзыва прежних: логин едет в профиле сессии, и клиент кладёт его к себе
    /// (шапка, список участников) без второго запроса. Время и способ входа новая сессия
    /// сохраняет (8.6): профиль сохраняет логин перед почтой, и смена почты в той же форме
    /// иначе упёрлась бы в «подтвердите, что это вы»
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> ChangeUserName([FromBody] ChangeUserNameDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await service.ChangeUserName(userId, dto.UserName, ct);
        return Ok(authResult.Build(await auth.GetUser(userId, ct), FreshLogin.Current(User)));
    }

    /// <summary>
    /// Подтвердить владение паролем (8.6) — перед привязкой, отвязкой, сменой почты
    /// и слиянием. Ответ — новая сессия с отметкой входа паролем: окно
    /// <c>AccountOptions.FreshLoginWindow</c> считается от неё. Прежние токены не отзываются.
    /// Неверный пароль — <c>422</c> по полю <c>password</c>. Неудачи считает лимит, общий со входом
    /// (8.1, <see cref="LoginLimiter"/>): сверх него — <c>400 auth.error.tooManyAttempts</c>
    /// и на верный пароль, иначе украденная сессия подбирала бы пароль без счёта
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> ConfirmPassword([FromBody] ConfirmPasswordDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var attempt = BeginPasswordAttempt(userId);

        if (!await service.VerifyPassword(userId, dto.Password, ct))
        {
            return UnprocessableEntity(new Dictionary<string, ValidationError[]>
            {
                ["password"] = [new ValidationError(AuthMessages.WrongPassword.Code, Text: AuthMessages.WrongPassword.Text)]
            });
        }

        attempt.Succeeded();
        return Ok(authResult.Build(await auth.GetUser(userId, ct), SessionLogin.Password()));
    }

    /// <summary>
    /// Задать пароль (8.1) — аккаунту без пароля, а в окне свежего входа через площадку
    /// (8.3) и с паролем, без старого, если площадка привязана не только что. Ответ — новая
    /// сессия: отметка отзыва завершила все прежние токены, включая тот, с которым пришёл
    /// этот запрос
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        // Свежий вход через Telegram или VK — новый пароль и без старого (8.3)
        await service.SetPassword(userId, dto.NewPassword, FreshExternalLogin, ct);
        return Ok(authResult.Build(await auth.GetUser(userId, ct)));
    }

    /// <summary>
    /// Сменить пароль (8.1). Неверный текущий — <c>422</c> по полю текущего пароля:
    /// отказ про одно поле формы, под ним и показывается. Неверный текущий считает лимит входа
    /// (8.1): сверх него — <c>400 auth.error.tooManyAttempts</c>. Ответ — новая сессия; текущий
    /// пароль в ней доказан — она же свежий вход паролем (8.6)
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var attempt = BeginPasswordAttempt(userId);

        if (!await service.ChangePassword(userId, dto.CurrentPassword, dto.NewPassword, ct))
        {
            return UnprocessableEntity(new Dictionary<string, ValidationError[]>
            {
                ["currentPassword"] = [new ValidationError(
                    AuthMessages.WrongPassword.Code,
                    Text: AuthMessages.WrongPassword.Text)]
            });
        }

        attempt.Succeeded();
        return Ok(authResult.Build(await auth.GetUser(userId, ct), SessionLogin.Password()));
    }

    /// <summary>
    /// «Объединить с другим аккаунтом» (7.1): владение вторым доказано его логином и паролем,
    /// его Telegram или VK. Ответ — обе карточки и ключ на подтверждение (7.4).
    /// Неверная пара логин/пароль — <c>422</c> по всему запросу: <c>401</c> выбросил бы
    /// из текущей сессии. Неудач больше лимита входа (8.1) — <c>400 auth.error.tooManyAttempts</c>.
    /// Вход площадки ничей — <c>400 auth.error.mergeNoAccount</c>,
    /// свой же аккаунт — <c>400 auth.error.mergeSameAccount</c>. Владение текущим — свежим
    /// входом (8.6): без него <c>400 auth.error.freshLoginRequired</c> до проверки второго.
    /// <see cref="Merge"/> свежести уже не спрашивает: ключ выдаётся только здесь и в <see cref="Link"/>
    /// </summary>
    [HttpPost]
    [Produces(typeof(MergePreviewDto))]
    public async Task<IActionResult> MergePreview([FromBody] MergeProofDto dto, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await RequireFreshLogin(userId, ct);
        Guid otherId;
        string platform;

        if (dto.External is { } external)
        {
            var identity = await externalProof.Resolve(external, ct)
                           ?? throw new BadRequestException(AuthMessages.ExternalFailed);
            var other = await auth.FindExternal(external.Provider!.Value, identity.Id, ct)
                        ?? throw new BadRequestException(AuthMessages.MergeNoAccount);
            otherId = other.Id;
            platform = FreshLogin.Method(external.Provider.Value);
        }
        else
        {
            // Подбор пароля чужого аккаунта отсюда считается тем же лимитом, что вход —
            // по учётке второго, чей это логин
            var login = new LoginDto { Login = dto.Login ?? string.Empty, Password = dto.Password ?? string.Empty };
            var attempt = loginLimiter.TryBeginProduct(await auth.UserIdOf(login, ct), Address())
                          ?? throw new BadRequestException(AuthMessages.TooManyAttempts);

            if (await auth.CheckPassword(login, ct) is not { } id)
            {
                return UnprocessableEntity(new Dictionary<string, ValidationError[]>
                {
                    [ValidationFields.Payload] = [new ValidationError(
                        AuthMessages.InvalidCredentials.Code, Text: AuthMessages.InvalidCredentials.Text)]
                });
            }

            attempt.Succeeded();
            otherId = id;
            // В журнал слияния — словом, как площадки (AccountMerge.Platform)
            platform = "password";
        }

        if (otherId == userId) throw new BadRequestException(AuthMessages.MergeSameAccount);

        return Ok(await merge.Preview(userId, otherId, platform, ct));
    }

    /// <summary>
    /// Слияние (7.4): ключ просмотра и какой аккаунт остаётся. Состав пространств
    /// поменялся — <c>409 auth.error.mergeChanged</c>, нужен новый просмотр. Ответ — новая
    /// сессия остающегося: остаётся не тот, из которого начинали, — клиент без неё оказался
    /// бы в удалённом; остаётся текущий — его права поменялись
    /// </summary>
    [HttpPost]
    [Produces(typeof(AuthenticationResultDto))]
    public async Task<IActionResult> Merge([FromBody] MergeDto dto, CancellationToken ct)
    {
        var keptId = await merge.Merge(User.GetUserId(), dto.Ticket, dto.KeepUserId, ct);
        return Ok(authResult.Build(await auth.GetUser(keptId, ct)));
    }

    /// <summary>
    /// Что будет при удалении (раздел 9): какие пространства удалятся с данными (где
    /// человек единственный участник), какие останутся остальным. Та же карточка,
    /// что у слияния
    /// </summary>
    [HttpGet]
    [Produces(typeof(AccountCardDto))]
    public async Task<IActionResult> DeletePreview(CancellationToken ct)
    {
        return Ok(await cards.Build(User.GetUserId(), ct));
    }

    /// <summary>
    /// Удалить аккаунт (раздел 9) — физически и сразу, окна «передумал» нет (раздел 10).
    /// Подтверждение — свой логин: не он — <c>400 auth.error.deleteConfirmMismatch</c>
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Delete([FromBody] DeleteAccountDto dto, CancellationToken ct)
    {
        await service.Delete(User.GetUserId(), dto.UserName, ct);
        return Ok();
    }

    /// <summary>
    /// Проверка своего пароля под лимитом входа (8.1) — по своей учётке и адресу. Заперто —
    /// <c>400 auth.error.tooManyAttempts</c> до проверки. Попытка уже в счёте неудачей,
    /// удачная зовёт <see cref="LoginLimiter.Attempt.Succeeded"/>
    /// </summary>
    private LoginLimiter.Attempt BeginPasswordAttempt(Guid userId) =>
        loginLimiter.TryBeginProduct(userId, Address())
        ?? throw new BadRequestException(AuthMessages.TooManyAttempts);

    private string Address() => ClientAddress.Key(HttpContext.Connection.RemoteIpAddress);

    private async Task<AccountLoginsDto> LoginsOf(Guid userId, CancellationToken ct) =>
        await WithFreshness(userId, await service.Logins(userId, ct), ct);

    /// <summary>Свежесть входа знает только токен — её дописывает контроллер (8.3, 8.6)</summary>
    private async Task<AccountLoginsDto> WithFreshness(Guid userId, AccountLoginsDto logins, CancellationToken ct)
    {
        logins.CanSetPasswordWithoutCurrent = await service.CanSetPasswordWithoutCurrent(userId, FreshExternalLogin, ct);
        logins.FreshLoginUntilUtc = await FreshLoginUntil(userId, ct);
        return logins;
    }

    /// <summary>
    /// До какого времени владение подтверждено (8.6): свежий вход паролем — или площадкой,
    /// привязанной не только что, как для пароля без старого (8.3). Иначе украденная сессия
    /// привязала бы свой Telegram, вошла им и дальше отвязывала и меняла почту сама
    /// </summary>
    private async Task<DateTime?> FreshLoginUntil(Guid userId, CancellationToken ct)
    {
        var window = options.Value.FreshLoginWindow;
        if (FreshLogin.Fresh(User, window) is not { } login) return null;

        var confirmed = login.Method == FreshLogin.PasswordMethod
                        || await service.CanSetPasswordWithoutCurrent(userId, FreshExternalLogin, ct);
        return confirmed ? (login.At + window).UtcDateTime : null;
    }

    /// <summary>Привязка, отвязка, смена почты и слияние — только в окне свежего входа (8.6)</summary>
    private async Task RequireFreshLogin(Guid userId, CancellationToken ct)
    {
        if (await FreshLoginUntil(userId, ct) == null) throw new BadRequestException(AuthMessages.FreshLoginRequired);
    }

    /// <summary>Окно свежего входа (8.3) — по <c>auth_time</c> токена, а не по времени его выдачи</summary>
    private ExternalProvider? FreshExternalLogin => FreshLogin.FreshProvider(User, options.Value.FreshLoginWindow);
}
