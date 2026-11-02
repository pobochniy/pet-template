using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Constants;

/// <summary>
/// Отказы регистрации и своего аккаунта (словарь <c>auth</c>). Раньше занятость ника, почты
/// и телефона приезжала 400-м с именем типа исключения в тексте (<c>ConstraintException</c>) —
/// теперь это 400 с кодом, как и прочие отказы.
///
/// Занятость ника неизбежна в любой системе с уникальными именами и ничего нового
/// о чужих аккаунтах не открывает
/// </summary>
public static class AuthMessages
{
    /// <summary>
    /// Логин занят — без учёта регистра (спец. accounts, 4.1). Тот же отказ у регистрации,
    /// регистрации по приглашению (<see cref="InvitationMessages.UserNameTaken"/>) и смены логина
    /// </summary>
    public static readonly ErrorMessage UserNameTaken = new(
        "auth.error.userNameTaken",
        "Такое имя пользователя уже занято");

    public static readonly ErrorMessage EmailTaken = new(
        "auth.error.emailTaken",
        "Эта почта уже зарегистрирована");

    public static readonly ErrorMessage PhoneTaken = new(
        "auth.error.phoneTaken",
        "Этот телефон уже зарегистрирован");

    /// <summary>
    /// Вход не удался. Одна фраза на «нет такого» и «не тот пароль»: подробности превратили
    /// бы форму входа в подтверждатель чужих логинов. Едет в 422 по всей форме
    /// (<see cref="ValidationFields.Payload"/>)
    /// </summary>
    public static readonly ErrorMessage InvalidCredentials = new(
        "auth.error.invalidCredentials",
        "Неправильное имя пользователя или пароль");

    /// <summary>
    /// «Сменить пароль» с неверным текущим (спец. accounts, 8.1). Едет в 422 по полю
    /// <c>currentPassword</c>: отказ про одно поле формы, под ним и показывается. Тот же
    /// отказ у <c>Account/ConfirmPassword</c> (8.6) — по полю <c>password</c>
    /// </summary>
    public static readonly ErrorMessage WrongPassword = new(
        "auth.error.wrongPassword",
        "Неверный текущий пароль");

    /// <summary>
    /// Неудачных проверок пароля больше лимита — на учётку или с адреса (спец. accounts, 8.1;
    /// <c>AdminLoginLimiter</c>): вход, подтверждение, смена пароля и проверка пароля второго
    /// аккаунта при слиянии заперты, пока не сползёт окно. Верный пароль в это время тоже не пускает
    /// </summary>
    public static readonly ErrorMessage TooManyAttempts = new(
        "auth.error.tooManyAttempts",
        "Слишком много неудачных попыток — попробуйте через 15 минут");

    /// <summary>
    /// Аккаунт с действующей ролью админки не удаляется (раздел 10): сначала роли снимает
    /// другой админ. При слиянии он может только остаться
    /// </summary>
    public static readonly ErrorMessage AdminCannotBeDeleted = new(
        "auth.error.adminCannotBeDeleted",
        "У аккаунта есть роль в админке — сначала её должен снять другой админ");

    /// <summary>
    /// Привязка, отвязка, смена почты или слияние без свежего входа (8.6): сессия могла
    /// попасть в чужие руки, а эти действия отдают аккаунт или снимают способы его вернуть
    /// </summary>
    public static readonly ErrorMessage FreshLoginRequired = new(
        "auth.error.freshLoginRequired",
        "Подтвердите, что это вы: введите пароль или войдите заново через Telegram или VK");

    /// <summary>
    /// Пароль без текущего через вход площадки, привязанный меньше
    /// <c>AccountOptions.RecentLinkWindow</c> назад (8.3): привязать мог и тот, кто украл сессию
    /// </summary>
    public static readonly ErrorMessage LoginRecentlyLinked = new(
        "auth.error.loginRecentlyLinked",
        "Этот вход привязан меньше суток назад — пока смените пароль, указав текущий");

    /// <summary>
    /// «Задать пароль» аккаунту, у которого он уже есть (8.1): для этого есть «Сменить»
    /// </summary>
    public static readonly ErrorMessage PasswordAlreadySet = new(
        "auth.error.passwordAlreadySet",
        "Пароль уже задан — смените его, указав текущий");

    /// <summary>Ссылка подтверждения или сброса не сработала: устарела, уже использована или подменена</summary>
    public static readonly ErrorMessage LinkInvalid = new(
        "auth.error.linkInvalid",
        "Ссылка устарела или уже использована — запросите новую");

    /// <summary>«Отправить ещё раз» без почты — нечего подтверждать</summary>
    public static readonly ErrorMessage NoEmail = new(
        "auth.error.noEmail",
        "Сначала укажите почту");

    /// <summary>
    /// Писем подтверждения больше лимита — одно в 2 минуты и 5 в сутки на аккаунт (8.4):
    /// «Отправить ещё раз» и смена почты
    /// </summary>
    public static readonly ErrorMessage TooManyEmails = new(
        "auth.error.tooManyEmails",
        "Письмо уже отправлено — подождите пару минут и проверьте почту, в том числе папку «Спам»");

    /// <summary>
    /// Отвязать последний способ войти (раздел 3): аккаунт стал бы недостижим
    /// </summary>
    public static readonly ErrorMessage LastLogin = new(
        "auth.error.lastLogin",
        "Это последний способ войти — сначала задайте пароль или привяжите другой вход");

    /// <summary>
    /// Площадка не подтвердила вход (5.4): подпись Telegram не сошлась, VK не обменял код
    /// или не ответил. Отказ входа, а не ошибка сервера
    /// </summary>
    public static readonly ErrorMessage ExternalFailed = new(
        "auth.error.externalFailed",
        "Не удалось войти через Telegram или VK. Попробуйте ещё раз");

    /// <summary>
    /// Восстановление через привязанный вход (8.3): этот Telegram или VK ни к одному
    /// аккаунту не привязан — новый не заводится
    /// </summary>
    public static readonly ErrorMessage LoginNotLinked = new(
        "auth.error.loginNotLinked",
        "Этот Telegram или VK не привязан ни к одному аккаунту — восстановите доступ по почте");

    /// <summary>Один Telegram и один VK на аккаунт (раздел 3): другой — сначала отвязать текущий</summary>
    public static readonly ErrorMessage LoginAlreadyLinked = new(
        "auth.error.loginAlreadyLinked",
        "К аккаунту уже привязан другой вход этой площадки — сначала отвяжите его");

    /// <summary>
    /// Привязываемый вход заняли другим аккаунтом между проверкой и записью (раздел 6):
    /// упёрлись в уникальный индекс. Отказ, а не слияние — владелец только что сменился,
    /// и человек повторит привязку, тогда уже со слиянием (раздел 7)
    /// </summary>
    public static readonly ErrorMessage LoginTaken = new(
        "auth.error.loginTaken",
        "Этот вход уже привязан к другому аккаунту");

    /// <summary>
    /// Mini-app открыт входом, которого шаблон ещё не знает, и без приглашения (5.6):
    /// аккаунт молча не заводится — клиент показывает развилку «Начать» / «У меня уже есть аккаунт»
    /// </summary>
    public static readonly ErrorMessage UnknownLogin = new(
        "auth.error.unknownLogin",
        "Этот вход ещё не привязан ни к одному аккаунту");

    /// <summary>Подтверждение удаления — свой логин (раздел 9); введён не он</summary>
    public static readonly ErrorMessage DeleteConfirmMismatch = new(
        "auth.error.deleteConfirmMismatch",
        "Введите свой логин, чтобы подтвердить удаление");

    /// <summary>Ключ слияния чужой, просрочен или уже использован (7.4) — просмотреть заново</summary>
    public static readonly ErrorMessage MergeTicketInvalid = new(
        "auth.error.mergeTicketInvalid",
        "Просмотр слияния устарел — откройте его заново");

    /// <summary>Между просмотром и подтверждением поменялся состав пространств (7.4) — <c>409</c></summary>
    public static readonly ErrorMessage MergeChanged = new(
        "auth.error.mergeChanged",
        "С тех пор поменялся состав пространств — посмотрите слияние заново");

    /// <summary>Доказали владение тем же аккаунтом, из которого начинали</summary>
    public static readonly ErrorMessage MergeSameAccount = new(
        "auth.error.mergeSameAccount",
        "Это тот же аккаунт, в который вы вошли");

    /// <summary>
    /// Слияние, в котором остаётся второй аккаунт, а второй заведён меньше
    /// <c>AccountOptions.MergeKeepOtherMinAge</c> назад (7.3): украденная сессия иначе сливала
    /// бы аккаунт жертвы в свежий аккаунт атакующего
    /// </summary>
    public static readonly ErrorMessage MergeKeepYoungAccount = new(
        "auth.error.mergeKeepYoungAccount",
        "Второй аккаунт заведён меньше суток назад — оставить можно только текущий");

    /// <summary>Вход площадки не принадлежит ни одному аккаунту — сливать не с чем, его можно привязать</summary>
    public static readonly ErrorMessage MergeNoAccount = new(
        "auth.error.mergeNoAccount",
        "Этот вход не привязан ни к одному аккаунту — его можно просто привязать");
}
