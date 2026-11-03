namespace PetTemplate.Shared.Options;

/// <summary>
/// Учёт расхода ассистента и лимиты пространств (спец. assistant, 13.5–13.6). Баланса,
/// пополнений и внутренней валюты в шаблоне нет: расход только считается, а лимит
/// отказывает новому ходу, когда пространство своё съело.
///
/// Умолчания стоят инициализаторами, как у <see cref="AssistantOptions"/>: с пустой
/// секцией приложение поднимается, а потолка у пространств нет вовсе
/// </summary>
public class SpendOptions
{
    /// <summary>
    /// Валюта прайса справочника моделей, платежей поставщику и денежных лимитов —
    /// одна на всё приложение. Только подпись: курсов и пересчёта нет
    /// </summary>
    public string Currency { get; set; } = "RUB";

    /// <summary>
    /// Лимит пространства по умолчанию — для тех, у кого нет своей строки
    /// <c>workspace_assistant_limits</c> (13.6). Пустое поле — без потолка
    /// </summary>
    public AssistantLimitOptions DefaultLimit { get; set; } = new();

    /// <summary>
    /// Потолок одного человека за сутки по UTC — по всем его пространствам вместе (13.6).
    /// Лимит пространства его не заменяет: пространство заводит кто угодно, и у каждого
    /// нового своё умолчание — без этого потолка десять пустых пространств дали бы
    /// десять суточных лимитов. Пустое поле — без потолка
    /// </summary>
    public AssistantDailyLimitOptions UserDailyLimit { get; set; } = new();

    /// <summary>
    /// Потолок всей установки за сутки по UTC — предохранитель счёта у поставщика (13.6):
    /// весь расход, служебный тоже. Достигнут — ассистент выключен для всех до конца суток
    /// тем же отказом, что «моделей нет» (<c>modelUnavailable</c>). Пустое поле — без потолка
    /// </summary>
    public AssistantDailyLimitOptions InstallationDailyLimit { get; set; } = new();

    /// <summary>
    /// Как часто пересчитываются метрики-снимки денег (<c>BillingMonitor</c>):
    /// остаток и сверка у поставщика
    /// </summary>
    public TimeSpan MonitorInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Порог остатка на счёте у поставщика (13.5): ниже — это уже инцидент, кончится
    /// счёт — ассистент встанет у всех разом
    /// </summary>
    public decimal ProviderBalanceAlarm { get; set; } = 1000m;
}

/// <summary>
/// Потолки расхода пространства (13.6). Любой непустой, который пространство уже
/// достигло, отказывает новому ходу; пустой — потолка нет
/// </summary>
public class AssistantLimitOptions
{
    /// <summary>Токенов (входящие и исходящие вместе) за сутки по UTC</summary>
    public long? TokensPerDay { get; set; }

    /// <summary>Токенов за календарный месяц по UTC</summary>
    public long? TokensPerMonth { get; set; }

    /// <summary>Денег за сутки по UTC, в <see cref="SpendOptions.Currency"/></summary>
    public decimal? CostPerDay { get; set; }

    /// <summary>Денег за календарный месяц по UTC</summary>
    public decimal? CostPerMonth { get; set; }
}

/// <summary>
/// Суточные потолки человека и установки (13.6); пустой — потолка нет
/// </summary>
public class AssistantDailyLimitOptions
{
    /// <summary>Токенов (входящие и исходящие вместе) за сутки по UTC</summary>
    public long? TokensPerDay { get; set; }

    /// <summary>Денег за сутки по UTC, в <see cref="SpendOptions.Currency"/></summary>
    public decimal? CostPerDay { get; set; }
}
