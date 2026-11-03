using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto.Admin;

/// <summary>
/// Признаки поиска <c>GET Admin/Dialogs</c> (спец. admin, 16.2). Поиска по тексту нет
/// намеренно: он превращает диагностику в чтение чужих писем.
///
/// <see cref="MinClarifications"/> и <see cref="MaxSatisfaction"/> читаются из автооценки
/// (16.3): прогон без оценки под такой фильтр не подходит
/// </summary>
public class AdminDialogFilterDto
{
    /// <summary>Первый день по UTC; по умолчанию — 7 дней до <see cref="To"/></summary>
    public DateOnly? From { get; set; }

    /// <summary>Последний день по UTC, включительно; по умолчанию — сегодня</summary>
    public DateOnly? To { get; set; }

    /// <summary>Идентификатор пространства или часть его названия</summary>
    public string? Workspace { get; set; }

    public Guid? ModelId { get; set; }

    public AssistantRunStatus? Status { get; set; }

    /// <summary>Не меньше стольких уточнений</summary>
    public int? MinClarifications { get; set; }

    /// <summary>Оценка не выше этой, 1–5: «покажи худшие»</summary>
    public int? MaxSatisfaction { get; set; }

    /// <summary>Курсор: последний прогон прошлой страницы, дальше — те, что раньше него</summary>
    public Guid? Before { get; set; }
}

/// <summary>Страница списка диалогов, новые сверху; <see cref="NextBefore"/> пусто — дальше нет</summary>
public class AdminDialogPageDto
{
    public AdminDialogDto[] Dialogs { get; set; } = [];

    public Guid? NextBefore { get; set; }
}

/// <summary>
/// Строка списка: прогон без содержимого (спец. assistant, 12.3). Живёт столько же,
/// сколько пространство, и старше срока трассы остаётся в списке — с моделью, счётчиками
/// и оценкой, но с <see cref="TraceExpired"/>
/// </summary>
public class AdminDialogDto
{
    public Guid RunId { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? FinishedUtc { get; set; }

    public Guid WorkspaceId { get; set; }

    public string WorkspaceName { get; set; } = null!;

    public Guid? ModelId { get; set; }

    /// <summary>Пусто у хода, которому некуда было идти: все модели разомкнуты или выключены</summary>
    public string? ModelName { get; set; }

    public AssistantRunStatus Status { get; set; }

    public AssistantStopReason? StopReason { get; set; }

    public int Steps { get; set; }

    public long TokensIn { get; set; }

    public long TokensOut { get; set; }

    /// <summary>Стоимость хода по прайсу — строка <c>run_spend</c>; ноль — ход без токенов</summary>
    public decimal Cost { get; set; }

    /// <summary>Из автооценки (16.3); пусто, пока прогон не оценён</summary>
    public int? Clarifications { get; set; }

    /// <summary>Из автооценки (16.3), 1–5; пусто, пока прогон не оценён</summary>
    public int? Satisfaction { get; set; }

    /// <summary>Трассу уже вычистили по сроку: открыть можно, показать нечего</summary>
    public bool TraceExpired { get; set; }
}

/// <summary>
/// Трасса одного прогона для разбора (16.2): то, что и так лежит в трассе, — что ушло
/// в модель, вызовы инструментов с аргументами, что вернулось, тайминги, токены, ошибка —
/// и автооценка, если она есть
/// </summary>
public class AdminDialogTraceDto
{
    public AdminDialogDto Dialog { get; set; } = null!;

    /// <summary>
    /// Содержимого уже нет: трасса старше <c>TraceRetentionDays</c> вычищена. Это ответ,
    /// а не ошибка — прогон при этом есть
    /// </summary>
    public bool Expired { get; set; }

    public AdminDialogStepDto[] Steps { get; set; } = [];

    public AdminDialogEvaluationDto? Evaluation { get; set; }
}

public class AdminDialogStepDto
{
    public int Step { get; set; }

    /// <summary><c>model</c> или <c>tool</c></summary>
    public string Kind { get; set; } = string.Empty;

    public string? ToolName { get; set; }

    /// <summary>Та же человеческая строка, что в «Показать ход мысли»</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Что ушло в модель или аргументы инструмента, усечено при записи до 4 КБ</summary>
    public string Input { get; set; } = string.Empty;

    public string? Output { get; set; }

    public int DurationMs { get; set; }

    public int TokensIn { get; set; }

    public int TokensOut { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedUtc { get; set; }
}

/// <summary>Что судья сказал о прогоне (16.3) — без переписки, только цифры и пара фраз</summary>
public class AdminDialogEvaluationDto
{
    public string ModelName { get; set; } = null!;

    public bool? Succeeded { get; set; }

    public int? Satisfaction { get; set; }

    public int? Clarifications { get; set; }

    public int? Replans { get; set; }

    public bool? AgentMistake { get; set; }

    public DialogStopReason? StopReason { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; }
}

/// <summary>Признаки <c>GET Admin/AccessLog</c> (16.2): админ и период</summary>
public class AdminAccessLogFilterDto
{
    public Guid? AdminUserId { get; set; }

    /// <summary>Первый день по UTC</summary>
    public DateOnly? From { get; set; }

    /// <summary>Последний день по UTC, включительно</summary>
    public DateOnly? To { get; set; }

    /// <summary>Курсор: последняя запись прошлой страницы</summary>
    public Guid? Before { get; set; }
}

public class AdminAccessLogPageDto
{
    public AdminAccessLogEntryDto[] Entries { get; set; } = [];

    public Guid? NextBefore { get; set; }
}

/// <summary>Кто, когда, чей прогон. Имена — пока их есть у кого спросить</summary>
public class AdminAccessLogEntryDto
{
    public Guid Id { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>Удалённый пользователь — пусто: клиент подписывает сам (спец. accounts, раздел 10)</summary>
    public Guid? AdminUserId { get; set; }

    public string? AdminName { get; set; }

    public Guid RunId { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>Пусто — пространство снесено, а запись о чтении его пережила</summary>
    public string? WorkspaceName { get; set; }
}
