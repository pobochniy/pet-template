using PetTemplate.Shared.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Лимит неудачных входов в админку (спец. admin, 3.3) — по учётке и по адресу
/// отдельно: первое держит подбор пароля к одному админу с многих адресов, второе —
/// перебор логинов с одного. Сверх <see cref="AdminOptions.AdminLoginAttempts"/>
/// за <see cref="AdminOptions.AdminLoginWindow"/> вход отвечает <c>404</c> даже
/// на верный пароль, пока окно не сползёт.
///
/// Попытка занимает место в счёте до проверки пароля — проверкой «заперто ли» и
/// записью под одной блокировкой, — а удачная его возвращает. Иначе N параллельных
/// запросов прошли бы проверку все, пока ни одна неудача ещё не записана.
///
/// Нужен именно здесь, а не только на nginx: <c>allow</c> по адресу этих дверей
/// не прикрывает в тот день, когда админку откроют «на время» (раздел 7). Вторая такая
/// дверь — принятие приглашения админа (3.4) — запирается этим же лимитером.
///
/// Логин считается по учётке, а не по строке: ник, почта и телефон одного человека —
/// один счётчик, иначе на подбор пароля лимит давался бы трижды. Логин, за которым
/// учётки нет, считается только по адресу: защищать там нечего, а сырая строка
/// любой длины в ключе позволила бы кому угодно растить память процесса.
///
/// Тем же счётом заперт и вход продукта (<see cref="TryBeginProduct"/>, спец. accounts, 8.1):
/// <c>Auth/LogIn</c> и <c>Auth/LogInAndLink</c>, проверка пароля второго аккаунта при слиянии,
/// <c>Account/ConfirmPassword</c> и текущий пароль в <c>Account/ChangePassword</c> — пароль
/// у админа тот же, и без этого лимит админки обходился бы подбором через продукт, а
/// украденная сессия подбирала бы пароль без счёта. Пороги продукта свои, мягче:
/// <see cref="ProductAttemptsPerUser"/> и <see cref="ProductAttemptsPerAddress"/> за то же окно, —
/// человек ошибается чаще админа, а за одним адресом бывает целый офис. Счётчики общие: пять
/// неудач в продукте запирают и вход этой учётки в админку.
///
/// Счёт в памяти процесса: API один, а рестарт, обнуляющий счётчики, подбору
/// пароля даёт одно окно — не больше, чем ожидание. Кэш свой, с потолком
/// <see cref="MaxEntries"/>: адресов у перебора может быть сколько угодно. Упёршись
/// в потолок, кэш сперва выселяет адреса, а счётчики учёток держит дольше
/// </summary>
public class AdminLoginLimiter(IOptions<AdminOptions> options) : IDisposable
{
    /// <summary>
    /// Потолок записей. Учёток и честных адресов у админки на порядки меньше; столько
    /// набирает только перебор с ротацией адресов. Дошли до потолка — место освобождается
    /// тут же, сжатием кэша (сначала истёкшие, потом адреса — у учёток приоритет выше), а не
    /// когда-нибудь фоном: иначе новая запись молча не легла бы, и попытка прошла бы без счёта
    /// </summary>
    internal const int MaxEntries = 100_000;

    /// <summary>Неудач входа продукта на учётку за окно — дальше вход заперт</summary>
    public const int ProductAttemptsPerUser = 10;

    /// <summary>Неудач входа продукта с одного адреса за окно</summary>
    public const int ProductAttemptsPerAddress = 30;

    private const string UserPrefix = "user:";

    private readonly AdminOptions _options = options.Value;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });

    private readonly Lock _gate = new();

    /// <summary>Сколько записей сейчас в счёте — для теста потолка</summary>
    internal int EntryCount => _cache.Count;

    /// <summary>
    /// Занять попытку по учётке и адресу. <c>userId</c> — чей логин (<c>null</c> — ничей,
    /// тогда только адрес). <c>null</c> в ответ — заперто, проверять пароль не надо.
    /// Иначе попытка уже в счёте как неудача — удачная зовёт <see cref="Attempt.Succeeded"/>
    /// </summary>
    public Attempt? TryBegin(Guid? userId, string address) =>
        Begin(userId, address, _options.AdminLoginAttempts, _options.AdminLoginAttempts);

    /// <summary>То же для входа продукта — с его порогами, но в тот же счёт</summary>
    public Attempt? TryBeginProduct(Guid? userId, string address) =>
        Begin(userId, address, ProductAttemptsPerUser, ProductAttemptsPerAddress);

    /// <summary>
    /// Только адрес — там, где логина нет: страница приглашения по токену (3.4). Перебор
    /// токенов с одного адреса запирает и вход с него же — счётчик общий
    /// </summary>
    public Attempt? TryBeginAddress(string address) =>
        Reserve((AddressKey(address), _options.AdminLoginAttempts));

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    private Attempt? Begin(Guid? userId, string address, int perUser, int perAddress) => userId == null
        ? Reserve((AddressKey(address), perAddress))
        : Reserve((UserKey(userId.Value), perUser), (AddressKey(address), perAddress));

    private Attempt? Reserve(params (string Key, int Limit)[] limits)
    {
        var now = DateTime.UtcNow;
        var since = now - _options.AdminLoginWindow;
        var keys = limits.Select(x => x.Key).ToArray();

        lock (_gate)
        {
            var counters = keys.Select(key => _cache.TryGetValue(key, out Attempts? x) ? x! : new Attempts()).ToArray();
            if (limits.Zip(counters).Any(x => x.Second.Count(since) >= x.First.Limit))
            {
                return null;
            }

            if (_cache.Count + keys.Length > MaxEntries) _cache.Compact(0.05);

            foreach (var (key, counter) in keys.Zip(counters))
            {
                counter.Add(now);
                _cache.Set(key, counter, new MemoryCacheEntryOptions
                {
                    // Запись переживает последнюю попытку ровно на окно — дальше она ничего не решает
                    AbsoluteExpirationRelativeToNow = _options.AdminLoginWindow,
                    Size = 1,
                    Priority = key.StartsWith(UserPrefix) ? CacheItemPriority.High : CacheItemPriority.Normal
                });
            }

            return new Attempt(this, counters, now);
        }
    }

    private void Release(Attempts[] counters, DateTime utc)
    {
        lock (_gate)
        {
            foreach (var counter in counters)
            {
                counter.Remove(utc);
            }
        }
    }

    private static string UserKey(Guid userId) => UserPrefix + userId;

    private static string AddressKey(string address) => "address:" + address;

    /// <summary>Занятая попытка: не отпущенная остаётся в счёте неудачей</summary>
    public sealed class Attempt
    {
        private readonly AdminLoginLimiter _limiter;

        private readonly Attempts[] _counters;

        private readonly DateTime _utc;

        internal Attempt(AdminLoginLimiter limiter, Attempts[] counters, DateTime utc)
        {
            _limiter = limiter;
            _counters = counters;
            _utc = utc;
        }

        public void Succeeded() => _limiter.Release(_counters, _utc);
    }

    internal sealed class Attempts
    {
        private readonly List<DateTime> _times = [];

        public void Add(DateTime utc) => _times.Add(utc);

        public void Remove(DateTime utc) => _times.Remove(utc);

        public int Count(DateTime sinceUtc)
        {
            _times.RemoveAll(x => x < sinceUtc);

            return _times.Count;
        }
    }
}
