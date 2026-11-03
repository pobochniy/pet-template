using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto.Assistant;

/// <summary>
/// Сообщение ленты (спец. assistant, 12.2). Всё, что человек видел на экране,
/// и ничего сверх: промежуточные вызовы инструментов живут в трассе (13.1)
/// </summary>
public class AssistantMessageDto
{
    public Guid Id { get; set; }

    public AssistantMessageRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Ход, породивший сообщение. Пусто у реплик человека</summary>
    public Guid? RunId { get; set; }

    /// <summary>
    /// План целиком: блок плана и отчёт «Готово» клиент рисует из него
    /// детерминированно, а не из прозы модели (5.1)
    /// </summary>
    public AssistantPlanDto? Plan { get; set; }

    /// <summary>
    /// Варианты ответа на уточняющий вопрос — чипсы под сообщением (5.2)
    /// </summary>
    public string[]? Options { get; set; }

    /// <summary>
    /// Факты, записанные этим ходом: строка «Запомнил: …» с кнопкой «Убрать» (12.5).
    /// Удалённый факт из неё пропадает, а сообщение остаётся
    /// </summary>
    public List<AssistantFactDto> Facts { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Факт памяти для экрана списка и для строки в ленте (12.5)
/// </summary>
public class AssistantFactDto
{
    public Guid Id { get; set; }

    public AssistantFactKind Kind { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Личный факт собеседника, а не общий для пространства: на экране списка они
    /// сгруппированы в «Про меня» и «Про пространство»
    /// </summary>
    public bool Personal { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Откуда взялся — чтобы дойти до разговора и трассы (13.1)</summary>
    public Guid? SourceRunId { get; set; }
}

/// <summary>
/// Экран списка фактов (12.5): плоский список, сгруппированный по владельцу —
/// «Про пространство» и «Про меня». Редактирования текста и добавления руками нет:
/// неверный факт удаляют и говорят ассистенту правильно
/// </summary>
public class AssistantMemoryDto
{
    public List<AssistantFactDto> Shared { get; set; } = [];

    public List<AssistantFactDto> Personal { get; set; } = [];
}

/// <summary>
/// Расшифровка голоса (6.1): текст уходит в поле ввода, а не в тред
/// </summary>
public class AssistantTranscriptionDto
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// План в том виде, в каком его читает клиент (12.2.1)
/// </summary>
public class AssistantPlanDto
{
    public Guid Id { get; set; }

    public AssistantPlanStatus Status { get; set; }

    /// <summary>Операции разобранным документом, а не строкой: рисовать блок по строке нечем</summary>
    public PlanDocument Operations { get; set; } = new();

    /// <summary>
    /// Что получилось: галочки, крестики и ссылки на созданное в отчёте «Готово».
    /// Пусто у плана, который ещё не применяли
    /// </summary>
    public PlanResult? Result { get; set; }

    public DateTime? AppliedAtUtc { get; set; }
}

/// <summary>
/// Состояние хода — то, что рисует плашку «шаг 7 из 30» (раздел 3)
/// </summary>
public class AssistantRunDto
{
    public Guid Id { get; set; }

    public AssistantRunStatus Status { get; set; }

    public int Step { get; set; }

    /// <summary>Потолок шагов из конфигурации: знаменатель в «шаг 7 из 30»</summary>
    public int StepsHard { get; set; }

    public bool Escalated { get; set; }

    /// <summary>
    /// Имя инструмента, а не текст: подпись «ищу в справке» собирает клиент
    /// своим словарём (раздел 3)
    /// </summary>
    public string? CurrentActivity { get; set; }

    public AssistantStopReason? StopReason { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>
    /// Итоговое сообщение хода: по нему клиент понимает, что в ленте появилось новое
    /// </summary>
    public Guid? MessageId { get; set; }
}

/// <summary>
/// Страница ленты (раздел 14). Грузится как в мессенджере: последние
/// <c>HistoryPageSize</c>, дальше — курсором вверх
/// </summary>
public class AssistantHistoryDto
{
    public List<AssistantMessageDto> Messages { get; set; } = [];

    /// <summary>Идущий ход треда, если он есть</summary>
    public AssistantRunDto? ActiveRun { get; set; }

    /// <summary>
    /// Что передать в <c>cursor</c> за следующей страницей. Пусто — выше ничего нет
    /// </summary>
    public Guid? NextCursor { get; set; }
}

/// <summary>Реплика человека</summary>
public class AssistantSendDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(AssistantSendLimits.TextMaxLength)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>Пределы реплики человека (раздел 14)</summary>
public static class AssistantSendLimits
{
    /// <summary>
    /// Реплика — фраза или абзац, а не документ: длиннее человек не наговорит
    /// и не наберёт, а лишняя длина съедает бюджет хода раньше, чем модель начнёт думать
    /// </summary>
    public const int TextMaxLength = 4000;
}

/// <summary>
/// Ответ на отправку: ход возвращается всегда, даже на одношаговую просьбу (раздел 3)
/// </summary>
public class AssistantSendResultDto
{
    public Guid RunId { get; set; }

    public Guid MessageId { get; set; }
}

/// <summary>
/// «Показать ход мысли» (спец. assistant, 13.2): шаги хода в читаемом виде
/// </summary>
public class AssistantTraceDto
{
    /// <summary>
    /// Трассу уже вычистили по сроку (13.1). Это не ошибка и не пустота:
    /// «подробности уже не хранятся» — отдельный ответ
    /// </summary>
    public bool Expired { get; set; }

    public List<AssistantTraceStepDto> Steps { get; set; } = [];
}

public class AssistantTraceStepDto
{
    public int Step { get; set; }

    /// <summary><c>model</c> или <c>tool</c></summary>
    public string Kind { get; set; } = string.Empty;

    public string? ToolName { get; set; }

    /// <summary>
    /// Человеческая строка: «искал в справке: как пригласить». Ни JSON,
    /// ни аргументов как есть — их человек глазами не читает
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    public int DurationMs { get; set; }

    public string? Error { get; set; }
}
