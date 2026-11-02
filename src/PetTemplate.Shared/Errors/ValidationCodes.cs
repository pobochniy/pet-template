namespace PetTemplate.Shared.Errors;

/// <summary>
/// Коды ошибок валидации — то, что уезжает в теле 422 вместо готовой фразы
/// (<see cref="ValidationError"/>). Текст живёт в словаре клиента, подстановки едут
/// рядом с кодом.
///
/// Первый сегмент кода — имя словаря на клиенте: <c>core</c> для правил, которые
/// встречаются на любом экране, имя экрана — для правил одного экрана. Что каждый
/// код есть в словаре, сверяет <c>npm run i18n:codes</c>
/// </summary>
public static class ValidationCodes
{
    /// <summary>
    /// Длина строки. Подстановки: <c>min</c> и <c>max</c> — обе или одна из них
    /// </summary>
    public const string StringLength = "core.validation.stringLength";

    /// <summary>
    /// Логин и пароль совпадают (<c>LoginDto</c>)
    /// </summary>
    public const string LoginEqualsPassword = "auth.validation.loginEqualsPassword";

    /// <summary>
    /// Логин не отвечает формату (спец. accounts, 4.1). Подстановки: <c>min</c> и <c>max</c>.
    ///
    /// Словарь <c>core</c>: логин спрашивают регистрация, приглашение и профиль — три
    /// экрана со своими словарями
    /// </summary>
    public const string UserNameFormat = "core.validation.userNameFormat";

    /// <summary>
    /// Логин вида <c>tg123</c> / <c>vk123</c> — зарезервирован под автоматические (4.1)
    /// </summary>
    public const string UserNameReserved = "core.validation.userNameReserved";

    /// <summary>
    /// Телефон не вида <c>+</c> и 11 цифр — таким не войти (<c>PhoneRule</c>). Словарь
    /// <c>core</c>: телефон спрашивают регистрация и профиль
    /// </summary>
    public const string PhoneFormat = "core.validation.phoneFormat";

    /// <summary>
    /// Язык вне белого списка (<c>SupportedLanguageAttribute</c>). Подстановка:
    /// <c>supported</c> — список кодов
    /// </summary>
    public const string Language = "core.validation.language";

    /// <summary>Не адрес почты (регистрация и профиль, спец. accounts, 8.4)</summary>
    public const string Email = "core.validation.email";

    /// <summary>
    /// Поле не заполнено. Отвечает встроенному <c>[Required]</c> — и явному,
    /// и тому, который ASP.NET подставляет не-nullable ссылочным свойствам сам
    /// (<c>ValidationCodeMessages</c>).
    ///
    /// Отдельно от клиентского <c>core.validation.required</c>: у того в тексте
    /// подстановка <c>{field}</c> — подпись поля, которую знает форма, а сервер нет
    /// </summary>
    public const string Required = "core.validation.valueRequired";

    /// <summary>
    /// Число вне диапазона. Подстановки: <c>min</c> и <c>max</c>
    /// </summary>
    public const string Range = "core.validation.range";

    /// <summary>
    /// Поле не совпало с тем, которое повторяет (встроенный <c>[Compare]</c>).
    /// Имя второго поля в подстановки не едет: человеку показывают отказ
    /// под самим полем, а имя свойства DTO подписью не является
    /// </summary>
    public const string Mismatch = "core.validation.mismatch";

    /// <summary>
    /// Элементов в наборе меньше минимума. Подстановка: <c>min</c>
    /// </summary>
    public const string MinItems = "core.validation.minItems";

    /// <summary>
    /// Элементов в наборе больше максимума. Подстановка: <c>max</c>
    /// </summary>
    public const string MaxItems = "core.validation.maxItems";

    /// <summary>
    /// Значение поля не легло в свой тип (число вне перечисления, строка вместо даты)
    /// </summary>
    public const string Invalid = "core.validation.invalid";

    /// <summary>
    /// Тело запроса — не JSON. Текст <c>System.Text.Json</c> наружу не едет: это утечка
    /// деталей парсера ради нулевой аудитории, а разработчику хватает лога
    /// </summary>
    public const string MalformedJson = "core.error.malformedJson";
}
