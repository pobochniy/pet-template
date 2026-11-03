using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto.Admin;

/// <summary>
/// Ответ <c>GET Admin/Billing/Summary</c> (спец. admin, 14): сколько ассистент стоит,
/// на что уходит и сходится ли наш расчёт с фактом поставщика. Деньги — в валюте прайса
/// справочника моделей (<see cref="Currency"/>); списаний и наценки нет — только учёт.
///
/// Период — дни по UTC, оба конца включительно. Выбранная модель сужает верхние
/// строки и разрез по пространствам; разрез по моделям идёт по всем — это и есть
/// разрез, и смешанной цифры в нём нет
/// </summary>
public class AdminBillingSummaryDto
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public Guid? ModelId { get; set; }

    /// <summary>Валюта прайса и лимитов — подпись, курсов нет</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Ходы с расходом за период</summary>
    public int Runs { get; set; }

    /// <summary>Токены ходов — входящие и исходящие вместе</summary>
    public long Tokens { get; set; }

    /// <summary>Стоимость ходов по прайсу поставщика — то, что записали при завершении хода</summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// Служебные расходы (компакция): ходят к тому же поставщику, но в лимит пространства
    /// не идут. Пока вызовов не было — ноль, но строка есть
    /// </summary>
    public decimal ServiceCost { get; set; }

    /// <summary>Разбивка служебных расходов по виду — все виды, и нулевые тоже</summary>
    public AdminServiceSpendDto[] Service { get; set; } = [];

    /// <summary>Всё вместе: ходы и служебные — столько ушло поставщикам по нашему расчёту</summary>
    public decimal TotalCost { get; set; }

    /// <summary>Лимит пространства по умолчанию — из конфигурации (спец. assistant, 13.6)</summary>
    public AdminAssistantLimitDto DefaultLimit { get; set; } = new();

    public AdminBillingModelDto[] Models { get; set; } = [];

    /// <summary>Кто съедает счёт — по убыванию стоимости, затем токенов</summary>
    public AdminBillingWorkspaceDto[] Workspaces { get; set; } = [];

    /// <summary>Сверка с фактом и счёт — по поставщику: платят поставщику, а не модели</summary>
    public AdminBillingProviderDto[] Providers { get; set; } = [];
}

/// <summary>Строка разреза по моделям</summary>
public class AdminBillingModelDto
{
    public Guid ModelId { get; set; }

    public string DisplayName { get; set; } = null!;

    public string Provider { get; set; } = null!;

    /// <summary>Ходы с расходом за период</summary>
    public int Runs { get; set; }

    /// <summary>Входящие и исходящие вместе</summary>
    public long Tokens { get; set; }

    public decimal Cost { get; set; }

    /// <summary>Служебные вызовы этой модели за период — входящие и исходящие вместе</summary>
    public long ServiceTokens { get; set; }

    public decimal ServiceCost { get; set; }
}

/// <summary>Строка служебных расходов одного вида за период</summary>
public class AdminServiceSpendDto
{
    public ServiceSpendKind Kind { get; set; }

    /// <summary>Сколько обращений к модели</summary>
    public int Calls { get; set; }

    /// <summary>Входящие и исходящие вместе</summary>
    public long Tokens { get; set; }

    public decimal Cost { get; set; }
}

/// <summary>
/// Строка разреза по пространствам. Имя настоящее — без него разрез не читается;
/// содержимого пространства здесь нет (спец. admin, 9). Удалённое пространство
/// остаётся строкой с идентификатором: его расход ушёл поставщику
/// </summary>
public class AdminBillingWorkspaceDto
{
    public Guid WorkspaceId { get; set; }

    /// <summary>Имя; <c>null</c> — пространство удалено</summary>
    public string? WorkspaceName { get; set; }

    public int Runs { get; set; }

    public long Tokens { get; set; }

    public decimal Cost { get; set; }

    /// <summary>У пространства свой лимит, а не умолчание</summary>
    public bool OwnLimit { get; set; }
}

/// <summary>
/// Ответ <c>GET Admin/Billing/Workspaces/{id}</c>: лимит пространства, сколько от него
/// съедено сейчас и кто из участников сколько потратил за период (спец. admin, 14)
/// </summary>
public class AdminBillingWorkspaceCardDto
{
    public Guid WorkspaceId { get; set; }

    public string WorkspaceName { get; set; } = null!;

    public string Currency { get; set; } = string.Empty;

    /// <summary>Свой лимит; <c>null</c> — действует умолчание</summary>
    public AdminAssistantLimitDto? OwnLimit { get; set; }

    public AdminAssistantLimitDto DefaultLimit { get; set; } = new();

    /// <summary>Действующий — свой или умолчание: против него проверяется <c>Send</c></summary>
    public AdminAssistantLimitDto Limit { get; set; } = new();

    /// <summary>Съедено за текущие сутки по UTC</summary>
    public AdminSpendUsageDto Today { get; set; } = new();

    /// <summary>Съедено за текущий календарный месяц по UTC</summary>
    public AdminSpendUsageDto Month { get; set; } = new();

    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    /// <summary>Кто сколько потратил за период — по убыванию стоимости</summary>
    public AdminBillingUserDto[] Users { get; set; } = [];

    /// <summary>Кто и когда правил свой лимит последним</summary>
    public DateTime? LimitUpdatedUtc { get; set; }

    public string? LimitUpdatedBy { get; set; }
}

/// <summary>Расход за отрезок</summary>
public class AdminSpendUsageDto
{
    public long Tokens { get; set; }

    public decimal Cost { get; set; }
}

/// <summary>Строка «кто сколько потратил» внутри пространства</summary>
public class AdminBillingUserDto
{
    public Guid UserId { get; set; }

    /// <summary>Логин, почта или телефон; <c>null</c> — аккаунт удалён</summary>
    public string? Name { get; set; }

    public int Runs { get; set; }

    public long Tokens { get; set; }

    public decimal Cost { get; set; }
}

/// <summary>
/// Потолки расхода пространства (спец. assistant, 13.6). Пустое поле — без потолка.
/// Тело <c>PUT Admin/Billing/Workspaces/{id}/Limit</c> — свой лимит целиком
/// </summary>
public class AdminAssistantLimitDto
{
    public long? TokensPerDay { get; set; }

    public long? TokensPerMonth { get; set; }

    public decimal? CostPerDay { get; set; }

    public decimal? CostPerMonth { get; set; }
}

/// <summary>
/// Сверка и счёт у одного поставщика.
///
/// Факт сравнивается с расчётом за <b>свои</b> периоды, а не за выбранный: факт
/// за сентябрь против расчёта за «последние 30 дней» разошёлся бы на ровном месте.
/// Поэтому в сверку попадают факты, чей период задевает выбранный, а расчёт
/// считается ровно по их дням
/// </summary>
public class AdminBillingProviderDto
{
    public string Provider { get; set; } = null!;

    /// <summary>Действующие факты, задевающие период, — по возрастанию периода</summary>
    public AdminLedgerFactDto[] Facts { get; set; } = [];

    /// <summary>Сумма фактов; <c>null</c> — факта за период не вводили</summary>
    public decimal? Fact { get; set; }

    /// <summary>Расчёт (ходы и служебные расходы) за дни фактов</summary>
    public decimal Estimate { get; set; }

    /// <summary>Расчёт минус факт; минус — считаем дешевле, чем берёт поставщик</summary>
    public decimal? Discrepancy { get; set; }

    public AdminProviderAccountDto Account { get; set; } = null!;
}

/// <summary>Действующий факт расхода — последний введённый за свой период</summary>
public class AdminLedgerFactDto
{
    public Guid Id { get; set; }

    public DateOnly PeriodFrom { get; set; }

    /// <summary>Включительно</summary>
    public DateOnly PeriodTo { get; set; }

    public decimal Amount { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string? ActorName { get; set; }
}

/// <summary>
/// Наш счёт у поставщика за всё время: пополнено минус потрачено. Потрачено — это
/// факты плюс расчёт за дни, которых не накрывает ни один факт: цифра из кабинета
/// запаздывает и вводится не за каждый период, а деньги на счёте кончаются по-настоящему
/// </summary>
public class AdminProviderAccountDto
{
    /// <summary>Платежей поставщику не записано — счёта у нас нет (своя карта, бесплатная модель)</summary>
    public bool HasPayments { get; set; }

    public decimal Paid { get; set; }

    /// <summary>Факты расхода за всё время</summary>
    public decimal Fact { get; set; }

    /// <summary>
    /// Расчёт за дни, которых не накрывает ни один факт: до первого, между фактами
    /// и после последнего
    /// </summary>
    public decimal Unbilled { get; set; }

    public decimal Remaining { get; set; }
}

/// <summary>
/// Тело <c>POST Admin/Billing/Ledger</c>: платёж поставщику или факт расхода.
/// Правка факта — запись с тем же периодом: действует последняя
/// </summary>
public class AdminLedgerEntryDto
{
    /// <summary>Код поставщика из справочника моделей</summary>
    public string Provider { get; set; } = string.Empty;

    public ProviderLedgerKind Kind { get; set; }

    /// <summary>
    /// У факта — не меньше нуля; у платежа — не ноль, а минус с причиной
    /// исправляет ошибочно записанный
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Только у факта, дни по UTC включительно</summary>
    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    public string? Comment { get; set; }
}
