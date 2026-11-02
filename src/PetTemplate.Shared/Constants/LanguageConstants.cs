namespace PetTemplate.Shared.Constants;

/// <summary>
/// Язык интерфейса и писем (спец. accounts, 8.4; mail, 3). Хранится кодом ISO 639-1
/// строкой, а не перечислением: числового стандарта у языков нет, а код нужен всем
/// потребителям как есть — <c>Intl.*</c>, имя словаря клиента, имя шаблона письма.
/// Здесь единственный на сервере белый список: код вне него отвергается, а не исправляется
/// </summary>
public static class LanguageConstants
{
    /// <summary>Русский — язык по умолчанию</summary>
    public const string Russian = "ru";

    public const string English = "en";

    /// <summary>Язык, который получает профиль без выбора</summary>
    public const string Default = Russian;

    /// <summary>
    /// Запас на теги с регионом (<c>en-GB</c>): колонка переживёт появление региональных
    /// кодов без миграции
    /// </summary>
    public const int CodeMaxLength = 5;

    /// <summary>
    /// Поддерживаемые языки. Расширение списка — правка здесь, в <c>shared/i18n/lang.ts</c>
    /// клиента и шаблон письма в <c>Mail/Templates</c>
    /// </summary>
    public static readonly IReadOnlyList<string> Supported = [Russian, English];

    /// <summary>Код поддерживается? Без учёта регистра: <c>RU</c> от клиента — тот же язык</summary>
    public static bool IsSupported(string? code) =>
        code is not null && Supported.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Код в том виде, в каком он ложится в профиль: поддерживаемый — строчными буквами,
    /// любой другой (включая отсутствующий) — <see cref="Default"/>.
    ///
    /// Нужен там, где язык приходит <b>вместе с регистрацией</b>: отбивать регистрацию
    /// из-за языка нельзя — его прислал браузер, а не выбрал человек. Там, где язык
    /// выбирают (<c>Account/SetLanguage</c>), незнакомый код — ошибка клиента, и её
    /// отбивает <c>SupportedLanguageAttribute</c>
    /// </summary>
    public static string Normalize(string? code) =>
        IsSupported(code) ? code!.ToLowerInvariant() : Default;
}
