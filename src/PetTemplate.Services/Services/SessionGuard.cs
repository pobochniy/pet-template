using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Services;

/// <summary>
/// Действует ли ещё токен (спец. accounts, 8.1, 10 и 11). Права живут в JWT,
/// и своего хранилища сессий нет — поэтому на каждом запросе сверяются две вещи:
///
/// <list type="bullet">
/// <item>аккаунт существует — у удалённого (раздел 10) токен перестаёт действовать сразу;</item>
/// <item>токен выпущен не раньше <see cref="User.TokensValidAfterUtc"/> — смена и сброс
/// пароля завершают остальные сессии.</item>
/// </list>
///
/// Стережёт и токен продукта, и админский (спец. admin, 3.3):
/// в админку входят паролем продукта, и его смена гасит обе сессии.
///
/// Отметка держится в памяти по id пользователя с коротким сроком: ходить в базу
/// на каждый запрос ради поля, которое меняется раз в год, незачем. Кто пишет отметку
/// или удаляет аккаунт, сбрасывает кэш сам (<see cref="Forget"/>), так что срок
/// нужен только как страховка. Отсутствие пользователя кэшируется так же
/// </summary>
public class SessionGuard(ApplicationContext db, IMemoryCache cache)
{
    /// <summary>Сколько живёт отметка в памяти без сброса</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>Отметка из базы: есть ли аккаунт и с какого момента действуют токены</summary>
    protected internal sealed record Entry(bool Exists, DateTime? ValidAfterUtc);

    private static string Key(Guid userId) => $"session-guard:{userId}";

    /// <summary>
    /// Поколение отметки: <see cref="Forget"/> меняет его до того, как выбросить отметку.
    /// Чтение из базы, начатое до сброса, кладёт отметку в кэш, только если поколение
    /// за это время не сменилось, — иначе прочитанное до смены пароля значение легло бы
    /// в кэш после сброса и прожило бы ещё <see cref="CacheTtl"/>
    /// </summary>
    private static string GenerationKey(Guid userId) => $"session-guard-gen:{userId}";

    /// <summary>
    /// <paramref name="issuedAtUtc"/> — <c>iat</c> токена; <c>null</c> — токен без времени
    /// выпуска (выпущен до появления отзыва): такой действует, пока отзывов не было
    /// </summary>
    public async Task<bool> IsValid(Guid userId, DateTime? issuedAtUtc, CancellationToken ct = default)
    {
        if (!cache.TryGetValue(Key(userId), out Entry? entry) || entry is null)
        {
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
        }

        if (!entry.Exists) return false;
        if (entry.ValidAfterUtc is not { } validAfter) return true;

        // Не строго: токен, выпущенный в ту же секунду, что отметка, — уже новый
        return issuedAtUtc is { } issued && issued >= validAfter;
    }

    /// <summary>Отметка из базы, мимо кэша</summary>
    protected internal virtual async Task<Entry> Load(Guid userId, CancellationToken ct)
    {
        var found = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.TokensValidAfterUtc })
            .SingleOrDefaultAsync(ct);

        return found == null ? new Entry(false, null) : new Entry(true, found.TokensValidAfterUtc);
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
    /// Сбросить отметку из памяти — после записи отзыва или удаления аккаунта. Поколение —
    /// первым: чтение, которое уже идёт, увидит смену и своё значение в кэше не оставит
    /// </summary>
    public void Forget(Guid userId)
    {
        cache.Remove(GenerationKey(userId));
        cache.Remove(Key(userId));
    }
}
