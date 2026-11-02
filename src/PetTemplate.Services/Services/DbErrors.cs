using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Services;

/// <summary>
/// Запись упала на уникальном индексе: значение заняли между проверкой и записью.
/// Проверка «занято?» перед записью отвечает на обычный случай понятным отказом,
/// а гонку двух одновременных запросов ловит индекс — и её отказ должен быть тем же,
/// а не 500.
///
/// Единственное место, где разбирается ошибка базы: код уникальности и имена индексов
/// зависят от СУБД, здесь — Postgres
/// </summary>
public static class DbErrors
{
    /// <summary>
    /// Уникальный индекс логина. Без учёта регистра — колонка <c>citext</c>
    /// (спец. accounts, 4.1); имя snake_case (спец. infrastructure, 3.1)
    /// </summary>
    public const string UserNameIndex = "ix_profiles_user_name";

    /// <summary>Уникальный индекс почты (спец. accounts, 8.4)</summary>
    public const string EmailIndex = "ix_profiles_email";

    /// <summary>Один Telegram — один аккаунт (спец. accounts, раздел 3)</summary>
    public const string TelegramIdIndex = "ix_users_telegram_id";

    /// <summary>Один VK — один аккаунт</summary>
    public const string VkIdIndex = "ix_users_vk_id";

    /// <summary>Ключ пары «человек + роль» админки (спец. admin, 5): двойная выдача</summary>
    public const string AdminRoleKey = "pk_user_in_admin_roles";

    /// <summary>
    /// Один живой ход на тред ассистента (спец. assistant, раздел 3): вторая отправка
    /// с другой вкладки проиграла гонку и встаёт в очередь треда
    /// </summary>
    public const string AssistantRunningIndex = AssistantRunConfiguration.RunningIndex;

    /// <summary>
    /// Ключ своего лимита ассистента у пространства (спец. assistant, 13.6): первую строку
    /// одновременно заводили два админа
    /// </summary>
    public const string WorkspaceLimitKey = WorkspaceAssistantLimitConfiguration.Key;

    /// <summary>
    /// Дедупликация факта памяти ассистента (спец. assistant, 12.5): тот же общий факт
    /// одновременно записал ход другого участника пространства
    /// </summary>
    public const string AssistantFactKeyIndex = AssistantFact.OwnerKeyIndex;

    /// <summary>
    /// Транзакция проиграла гонку за замки — где-то в цепочке исключений: EF заворачивает
    /// его по-разному у чтения и записи. Postgres отвечает <c>40P01</c> (взаимная блокировка,
    /// транзакция выбрана жертвой и откачена) или <c>55P03</c> (замок не дождались за
    /// <c>lock_timeout</c>). Для того, кто держит в транзакции чужие строки под замком
    /// (слияние, спец. accounts, 7.4), это значит «одновременно с вами меняли то же самое» —
    /// отказ, а не 500
    /// </summary>
    public static bool IsLockConflict(Exception ex)
    {
        for (var inner = ex; inner != null; inner = inner.InnerException)
        {
            if (inner is PostgresException
                {
                    SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.LockNotAvailable
                })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Запись упала на уникальном индексе <paramref name="index"/>: Postgres отвечает
    /// <c>23505</c> (<see cref="PostgresErrorCodes.UniqueViolation"/>) и называет
    /// ограничение по имени — разбирать текст сообщения не нужно
    /// </summary>
    public static bool IsDuplicate(DbUpdateException ex, string index) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == index;
}
