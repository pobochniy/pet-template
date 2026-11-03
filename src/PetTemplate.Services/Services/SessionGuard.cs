using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Services;

/// <summary>
/// Действует ли ещё токен и что им можно (спец. accounts, 8.1, 10 и 11; authorization, 2.3).
/// Своего хранилища сессий нет, а прав в токене нет — поэтому на каждом запросе из базы
/// берётся одна отметка:
///
/// <list type="bullet">
/// <item>аккаунт существует — у удалённого (раздел 10) токен перестаёт действовать сразу;</item>
/// <item>токен выпущен не раньше <see cref="User.TokensValidAfterUtc"/> — смена и сброс
/// пароля завершают остальные сессии;</item>
/// <item>права по пространствам — их проверяет <c>[AuthorizePermissions]</c>
/// (<see cref="PermissionsIn"/>): отозванное право и удалённый участник отбиваются
/// со следующего запроса, а не с истечением токена.</item>
/// </list>
///
/// Стережёт и токен продукта, и админский (спец. admin, 3.3):
/// в админку входят паролем продукта, и его смена гасит обе сессии.
///
/// Отметка держится в памяти процесса по id пользователя с коротким сроком
/// (<c>tech-debt/single-instance.md</c>). Кто меняет отметку, состав или права пространства
/// (смена пароля, удаление аккаунта, выдача и отзыв прав, выход, приглашение), после
/// сохранения сбрасывает кэш сам (<see cref="Forget"/>), так что срок нужен только как
/// страховка. Отсутствие пользователя кэшируется так же
/// </summary>
public class SessionGuard(ApplicationContext db, IMemoryCache cache)
{
    /// <summary>Сколько живёт отметка в памяти без сброса</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Отметка из базы: есть ли аккаунт, с какого момента действуют токены и права
    /// по пространствам — <c>{workspaceId → права}</c>
    /// </summary>
    protected internal sealed record Entry(
        bool Exists,
        DateTime? ValidAfterUtc,
        IReadOnlyDictionary<Guid, PermissionEnum[]> Permissions);

    private static string Key(Guid userId) => $"session-guard:{userId}";

    /// <summary>
    /// Поколение отметки: <see cref="Forget"/> меняет его до того, как выбросить отметку.
    /// Чтение из базы, начатое до сброса, кладёт отметку в кэш, только если поколение
    /// за это время не сменилось, — иначе прочитанное до смены пароля (или до отзыва права)
    /// значение легло бы в кэш после сброса и прожило бы ещё <see cref="CacheTtl"/>
    /// </summary>
    private static string GenerationKey(Guid userId) => $"session-guard-gen:{userId}";

    /// <summary>
    /// <paramref name="issuedAtUtc"/> — <c>iat</c> токена; <c>null</c> — токен без времени
    /// выпуска (выпущен до появления отзыва): такой действует, пока отзывов не было
    /// </summary>
    public async Task<bool> IsValid(Guid userId, DateTime? issuedAtUtc, CancellationToken ct = default)
    {
        var entry = await Get(userId, ct);

        if (!entry.Exists) return false;
        if (entry.ValidAfterUtc is not { } validAfter) return true;

        // Не строго: токен, выпущенный в ту же секунду, что отметка, — уже новый
        return issuedAtUtc is { } issued && issued >= validAfter;
    }

    /// <summary>
    /// Права пользователя в пространстве — из той же отметки, что и <see cref="IsValid"/>.
    /// Пустой набор — не участник: членство и есть права (спец. authorization, 1)
    /// </summary>
    public async Task<PermissionEnum[]> PermissionsIn(Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        var entry = await Get(userId, ct);

        return entry.Permissions.TryGetValue(workspaceId, out var permissions) ? permissions : [];
    }

    /// <summary>
    /// Завершить все сессии аккаунта, кроме той, что получит токен после этого.
    /// Только ставит отметку — сохраняет вызывающий, вместе со своим изменением
    /// (сменой пароля, сбросом), а после сохранения зовёт <see cref="Forget"/>.
    ///
    /// Отметка — целые секунды вниз: <c>iat</c> в JWT тоже в секундах, и токен, выданный
    /// ответом на тот же запрос, иначе отбился бы как «выпущенный раньше»
    /// </summary>
    public static void Revoke(User user, DateTime nowUtc)
    {
        user.TokensValidAfterUtc = new DateTime(nowUtc.Ticks - nowUtc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    /// <summary>
    /// Сбросить отметку из памяти — после коммита отзыва, удаления аккаунта или правки
    /// состава и прав пространства. До коммита звать нельзя: чтение между сбросом и коммитом
    /// положит в кэш ещё старое. Поколение — первым: чтение, которое уже идёт, увидит смену
    /// и своё значение в кэше не оставит
    /// </summary>
    public void Forget(Guid userId)
    {
        cache.Remove(GenerationKey(userId));
        cache.Remove(Key(userId));
    }

    /// <summary>То же для нескольких: каждого, чьи права поменяло одно сохранение</summary>
    public void Forget(IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds)
        {
            Forget(userId);
        }
    }

    /// <summary>Отметка из базы, мимо кэша</summary>
    protected internal virtual async Task<Entry> Load(Guid userId, CancellationToken ct)
    {
        var found = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.TokensValidAfterUtc,
                Permissions = x.UserPermissions.Select(p => new { p.WorkspaceId, p.PermissionId }).ToList()
            })
            .SingleOrDefaultAsync(ct);

        if (found == null) return new Entry(false, null, new Dictionary<Guid, PermissionEnum[]>());

        var permissions = found.Permissions
            .GroupBy(x => x.WorkspaceId)
            .ToDictionary(x => x.Key, x => x.Select(p => p.PermissionId).Distinct().ToArray());

        return new Entry(true, found.TokensValidAfterUtc, permissions);
    }

    /// <summary>Отметка из кэша, а нет её там — из базы, с проверкой поколения</summary>
    private async Task<Entry> Get(Guid userId, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(userId), out Entry? entry) && entry is not null) return entry;

        var generation = cache.GetOrCreate(GenerationKey(userId), item =>
        {
            // Живёт дольше отметки: пропало поколение — отметка просто не кэшируется
            item.SlidingExpiration = CacheTtl * 2;
            return new object();
        })!;

        entry = await Load(userId, ct);

        // Сначала кладём, потом сверяем поколение: сброс, случившийся между сверкой
        // и записью, иначе прошёл бы незамеченным. Сменилось — убираем своё
        cache.Set(Key(userId), entry, CacheTtl);
        if (!cache.TryGetValue(GenerationKey(userId), out object? current) || !ReferenceEquals(current, generation))
        {
            cache.Remove(Key(userId));
        }

        return entry;
    }
}
