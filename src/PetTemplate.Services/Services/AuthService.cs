using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Services.Services;

/// <summary>
/// Вход и регистрация. Пользователь наружу — всегда <see cref="UserDto"/> одним кодом
/// (<see cref="BuildUser"/>): права по пространствам и посчитанное основное пространство
/// едут одинаково, каким бы путём человек ни вошёл (спец. authorization, 2.1)
/// </summary>
public class AuthService(ApplicationContext db, WorkspaceService workspaces, EmailConfirmation emails,
    UserNameGenerator userNames)
{
    /// <summary>
    /// Регистрация по паролю. С названием пространства — вместе с первым пространством,
    /// одним сохранением (спец. authorization, 3, п. 1): сбой посередине не должен
    /// оставить аккаунт без пространства, о котором человек только что попросил.
    ///
    /// Почта в форме — сразу письмо подтверждения (спец. accounts, 8.4), тем же сохранением,
    /// что и аккаунт (outbox). Сверх лимита аккаунт всё равно заводится
    /// </summary>
    public async Task Register(RegisterDto dto, CancellationToken ct)
    {
        var profile = await CreateProfile(dto.UserName, dto.Password, dto.Email, dto.Phone, dto.Language, ct);

        if (profile.Email != null)
        {
            await emails.Request(profile.Id, profile.Email, ct);
        }

        if (!string.IsNullOrWhiteSpace(dto.WorkspaceName))
        {
            // Пространств у только что заведённого нет — новое становится основным
            await workspaces.Add(dto.WorkspaceName.Trim(), profile.Id, primaryOf: profile, ct);
        }

        await SaveNewProfile(ct);
    }

    /// <summary>
    /// Заводит профиль с паролем в трекере <c>DbContext</c> — <b>без</b> <c>SaveChanges</c>:
    /// сохраняет вызывающий, и это не мелочь. Регистрация с пространством и по приглашению
    /// дописывают права и указатель на основное пространство тем же сохранением, чтобы
    /// пользователь не мог остаться зарегистрированным, но без того, о чём просил.
    ///
    /// Занятые логин, почта и телефон — <c>400</c> с кодом (<see cref="AuthMessages"/>).
    /// Логин и почта сравниваются без учёта регистра — колонки <c>citext</c> (спец. accounts, 4.1).
    /// Гонку двух одновременных регистраций ловит уникальный индекс — <see cref="SaveNewProfile"/>
    /// </summary>
    public async Task<Profile> CreateProfile(string userName, string password, string? email, string? phone,
        string? language, CancellationToken ct)
    {
        userName = userName.Trim();
        // Пробелы по краям — опечатка копирования: с ними адрес не совпал бы ни при входе,
        // ни при проверке занятости. Пустая — нет почты
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

        if (await IsUserNameTaken(userName, ct))
        {
            throw new BadRequestException(AuthMessages.UserNameTaken);
        }

        if (email != null && await db.Profiles.AnyAsync(x => x.Email == email, ct))
        {
            throw new BadRequestException(AuthMessages.EmailTaken);
        }

        if (phone != null && await db.Profiles.AnyAsync(x => x.PhoneNumber == phone, ct))
        {
            throw new BadRequestException(AuthMessages.PhoneTaken);
        }

        var id = Guid.CreateVersion7();
        var profile = new Profile
        {
            Id = id,
            AvatarId = Random.Shared.Next(0, 8),
            UserName = userName,
            Email = email,
            PhoneNumber = phone,
            // Язык прислал браузер, а не выбрал человек: незнакомый — по умолчанию, не отказ (8.4)
            Language = LanguageConstants.Normalize(language),
            User = new User
            {
                Id = id,
                CreatedAtUtc = DateTime.UtcNow
            }
        };
        PasswordHasher.Set(profile.User, password);

        await db.Profiles.AddAsync(profile, ct);

        return profile;
    }

    /// <summary>
    /// Сохранение нового профиля: логин или почту заняли между проверкой и записью —
    /// тот же отказ, что и у проверки, а не 500
    /// </summary>
    public async Task SaveNewProfile(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsDuplicate(ex, DbErrors.UserNameIndex))
        {
            throw new BadRequestException(AuthMessages.UserNameTaken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsDuplicate(ex, DbErrors.EmailIndex))
        {
            throw new BadRequestException(AuthMessages.EmailTaken);
        }
    }

    /// <summary>
    /// Логин занят — без учёта регистра (4.1): <c>Masha</c> занят, если есть <c>masha</c>.
    /// Общий для регистрации и регистрации по приглашению
    /// </summary>
    public Task<bool> IsUserNameTaken(string userName, CancellationToken ct = default)
    {
        var trimmed = userName.Trim();
        return db.Profiles.AnyAsync(x => x.UserName == trimmed, ct);
    }

    /// <summary>
    /// Чей это логин — с проверкой пароля, но не вход: ни <c>LastLoginAtUtc</c>, ни пересчёта
    /// хеша. Для доказательства владения вторым аккаунтом при слиянии (спец. accounts, 7):
    /// поле «последний вход» в карточке поддержки не должно меняться от чужой проверки.
    /// Пароль не подошёл — <c>null</c>
    /// </summary>
    public async Task<Guid?> CheckPassword(LoginDto dto, CancellationToken ct = default)
    {
        var profile = await ByLogin(dto).Include(x => x.User).AsNoTracking().SingleOrDefaultAsync(ct);
        return profile != null && PasswordHasher.Verify(profile.User!, dto.Password) ? profile.Id : null;
    }

    /// <summary>
    /// Чей это логин — без проверки пароля. Ник, почта и телефон одной учётки отвечают одним
    /// и тем же идентификатором: по нему лимит входа (<c>LoginLimiter</c>, спец. accounts, 8.1;
    /// admin, 3.3) считает попытки. <c>null</c> — такого логина нет
    /// </summary>
    public async Task<Guid?> UserIdOf(LoginDto dto, CancellationToken ct = default) =>
        await ByLogin(dto).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);

    /// <summary>
    /// Вход по паролю. Хеш прежней схемы переезжает на новую тут же (спец. accounts, 8.1):
    /// пароль в руках только сейчас. Время входа — для карточки поддержки (8.5)
    /// </summary>
    public async Task<UserDto> LogIn(LoginDto dto, CancellationToken ct)
    {
        var profile = await ByLogin(dto).Include(x => x.User).SingleOrDefaultAsync(ct);

        if (profile == null || !PasswordHasher.Verify(profile.User!, dto.Password))
            throw new UnauthorizedAccessException();

        if (PasswordHasher.NeedsRehash(profile.User!))
        {
            PasswordHasher.Set(profile.User!, dto.Password);
        }

        profile.User!.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await BuildUser(profile, ct);
    }

    /// <summary>
    /// Пользователь с правами — тем же составом, что и после входа. Нужен там, где токен
    /// выдаётся не по логину: создание пространства, выход из него, принятие приглашения,
    /// смена логина и пароля перевыпускают сессию (спец. authorization, 2.2)
    /// </summary>
    public async Task<UserDto> GetUser(Guid userId, CancellationToken ct = default)
    {
        var profile = await db.Profiles.Include(x => x.User).SingleAsync(x => x.Id == userId, ct);

        return await BuildUser(profile, ct);
    }

    /// <summary>
    /// Вход через Telegram — с сайта и из mini-app (спец. accounts, 5.2 и 5.6): известный
    /// вход — в свой аккаунт, неизвестный заводит аккаунт без пароля с автоматическим логином
    /// (<see cref="UserNameGenerator"/>, 4.2) и признаком <see cref="UserDto.IsNewAccount"/>.
    /// <paramref name="telegramUserName"/> — <c>@username</c> Telegram, кандидат в логин;
    /// <paramref name="language"/> — язык интерфейса, с которым пришли: пишется только
    /// при заведении, следующие входы его не трогают
    /// </summary>
    public async Task<UserDto> GetOrRegisterUser(long telegramId, string? telegramUserName, string? language,
        CancellationToken ct = default)
    {
        var known = await FindExternal(ExternalProvider.Telegram, telegramId, ct);
        if (known != null) return known;

        var profile = NewExternalProfile(
            await userNames.ForTelegram(telegramId, telegramUserName, ct),
            language,
            new User { TelegramId = telegramId });

        return await RegisterExternal(profile, ExternalProvider.Telegram, telegramId, ct);
    }

    /// <summary>
    /// Вход через VK — с сайта (VK ID) и из mini-app. Имени пользователя VK не отдаёт —
    /// логин сразу <c>vk&lt;id&gt;</c> (4.2). <paramref name="language"/> — у mini-app
    /// <c>vk_language</c>: VK называет язык своего интерфейса
    /// </summary>
    public async Task<UserDto> GetOrRegisterUserForVk(long vkUserId, string? language, CancellationToken ct = default)
    {
        var known = await FindExternal(ExternalProvider.Vk, vkUserId, ct);
        if (known != null) return known;

        var profile = NewExternalProfile(
            await userNames.Fallback(UserNameRule.ForVk(vkUserId), ct),
            language,
            new User { VkId = vkUserId });

        return await RegisterExternal(profile, ExternalProvider.Vk, vkUserId, ct);
    }

    /// <summary>
    /// Аккаунт, к которому привязан этот вход площадки, — с отметкой входа (8.5); нет такого —
    /// <c>null</c>, и ничего не заводится: так входит восстановление доступа (8.3), а mini-app
    /// без приглашения сначала спрашивает человека (развилка 5.6)
    /// </summary>
    public async Task<UserDto?> FindExternal(ExternalProvider provider, long externalId, CancellationToken ct = default)
    {
        var profile = await db.Profiles
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => provider == ExternalProvider.Telegram
                ? x.User!.TelegramId == externalId
                : x.User!.VkId == externalId, ct);
        if (profile == null) return null;

        return await StampLogin(profile, ct);
    }

    /// <summary>
    /// Профиль аккаунта, заведённого входом через Telegram или VK: без пароля,
    /// с автоматическим логином
    /// </summary>
    private static Profile NewExternalProfile(string userName, string? language, User user)
    {
        var id = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        user.Id = id;
        user.CreatedAtUtc = now;
        user.LastLoginAtUtc = now;

        return new Profile
        {
            Id = id,
            AvatarId = Random.Shared.Next(0, 8),
            UserName = userName,
            UserNameIsGenerated = true,
            // Язык прислал браузер или площадка, а не выбрал человек: незнакомый — по умолчанию
            Language = LanguageConstants.Normalize(language),
            User = user
        };
    }

    /// <summary>
    /// Сохранить аккаунт, заведённый входом площадки. Два одновременных первых входа одним
    /// Telegram (VK) — двойной клик, две вкладки, два открытых mini-app — оба не нашли аккаунт
    /// и оба заводят: второй падает на уникальном индексе <c>TelegramId</c> (<c>VkId</c>). Это не
    /// ошибка, а тот же человек — он входит в аккаунт, который завёл первый (4.2)
    /// </summary>
    private async Task<UserDto> RegisterExternal(Profile profile, ExternalProvider provider, long externalId,
        CancellationToken ct)
    {
        await db.Profiles.AddAsync(profile, ct);

        try
        {
            var fallback = provider == ExternalProvider.Telegram
                ? UserNameRule.ForTelegram(externalId)
                : UserNameRule.ForVk(externalId);
            await SaveExternalProfile(profile, fallback, ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsDuplicate(ex,
                                               provider == ExternalProvider.Telegram ? DbErrors.TelegramIdIndex : DbErrors.VkIdIndex))
        {
            db.Entry(profile.User!).State = EntityState.Detached;
            db.Entry(profile).State = EntityState.Detached;

            return await FindExternal(provider, externalId, ct)
                   ?? throw new InvalidOperationException($"{provider} {externalId} is taken but not found");
        }

        var created = await BuildUser(profile, ct);
        created.IsNewAccount = true;
        return created;
    }

    /// <summary>
    /// Сохранить новый профиль, а если логин успели занять между проверкой и записью —
    /// взять следующий свободный запасной (<see cref="UserNameGenerator.Fallback"/>).
    /// <c>tg&lt;id&gt;</c> не свободен «по построению»: отвязанный вход оставляет логин
    /// прежнему аккаунту (раздел 6), и тот же Telegram, придя снова, получает хвост
    /// </summary>
    private async Task SaveExternalProfile(Profile profile, string fallback, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (DbErrors.IsDuplicate(ex, DbErrors.UserNameIndex) && attempt < 3)
            {
                profile.UserName = await userNames.Fallback(fallback, ct);
            }
        }
    }

    /// <summary>Отметка входа (спец. accounts, 8.5) — и у входа через площадку</summary>
    private async Task<UserDto> StampLogin(Profile profile, CancellationToken ct)
    {
        profile.User!.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await BuildUser(profile, ct);
    }

    private IQueryable<Profile> ByLogin(LoginDto dto)
    {
        if (dto.IsPhone) return db.Profiles.Where(x => x.PhoneNumber == dto.Login);

        // Почта уникальна и подтверждённой быть не обязана (спец. accounts, 8.4)
        if (dto.IsEmail)
        {
            var email = dto.Login.Trim();
            return db.Profiles.Where(x => x.Email == email);
        }

        // Логин сравнивается без учёта регистра — так же, как проверяется его занятость (`citext`)
        var userName = dto.Login.Trim();
        return db.Profiles.Where(x => x.UserName == userName);
    }

    /// <summary>
    /// Единственное место, где профиль превращается в <see cref="UserDto"/>:
    /// пароль, телеграм, вк и перевыпуск сессии выдают один и тот же ответ
    /// </summary>
    private async Task<UserDto> BuildUser(Profile profile, CancellationToken ct)
    {
        var (permissionsByWorkspace, primaryWorkspaceId) = await LoadWorkspaceContext(profile, ct);

        return new UserDto
        {
            Id = profile.Id,
            // Посчитанное, а не из профиля: указатель мог устареть
            Profile = profile.ToDto(primaryWorkspaceId),
            PermissionsByWorkspace = permissionsByWorkspace
        };
    }

    /// <summary>
    /// Права по пространствам и основное пространство — одним куском, потому что второе
    /// без первого не считается.
    ///
    /// Ничего не пишет и профиль не меняет: это чтение (спец. authorization, 7.1).
    /// Указатель держат в порядке те, кто меняет состав пространств: первое членство
    /// ставит его сразу (<see cref="WorkspaceService.Add"/>, принятие приглашения),
    /// а потерю членства чинят <see cref="WorkspaceService.Leave"/>,
    /// <see cref="WorkspaceService.RemoveMember"/> и чистка заброшенных — тем же
    /// сохранением, что снимает права.
    ///
    /// Расхождение остаётся только тем, чего мы не предусмотрели, — и на этот случай
    /// в ответ едет доступное пространство, а не мёртвый указатель: пускать человека
    /// в пространство, которого у него нет, хуже, чем открыть ему другое своё
    /// </summary>
    private async Task<(Dictionary<Guid, List<PermissionEnum>> permissions, Guid? primaryWorkspaceId)> LoadWorkspaceContext(
        Profile profile,
        CancellationToken ct)
    {
        var userPermissions = await db.UserInPermissions
            .Where(x => x.UserId == profile.Id)
            .Select(x => new { x.WorkspaceId, x.PermissionId })
            .ToListAsync(ct);

        var permissionsByWorkspace = userPermissions
            .GroupBy(x => x.WorkspaceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.PermissionId).ToList());

        var primaryIsAvailable = profile.PrimaryWorkspaceId.HasValue
                                 && permissionsByWorkspace.ContainsKey(profile.PrimaryWorkspaceId.Value);

        // Пространств не осталось вовсе — указателя тоже: пользователь увидит
        // приветственную плашку (спец. authorization, 3, п. 3)
        var effectivePrimary = primaryIsAvailable
            ? profile.PrimaryWorkspaceId
            : permissionsByWorkspace.Count > 0
                ? permissionsByWorkspace.Keys.First()
                : null;

        return (permissionsByWorkspace, effectivePrimary);
    }
}
