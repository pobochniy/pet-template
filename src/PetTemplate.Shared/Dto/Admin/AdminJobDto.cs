using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto.Admin;

/// <summary>
/// Запуск из админки (спец. admin, 15.3 и 16.3): сверка модели или автооценка пачки
/// диалогов. Идёт фоном — экран перечитывает его, пока статус <c>Running</c>
/// </summary>
public class AdminJobDto
{
    public Guid Id { get; set; }

    public AdminJobKind Kind { get; set; }

    public Guid ModelId { get; set; }

    public string ModelName { get; set; } = null!;

    /// <summary>Кто запустил — логин; ушедший админ — пусто</summary>
    public string? StartedBy { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? FinishedUtc { get; set; }

    public AdminJobStatus Status { get; set; }

    public int Planned { get; set; }

    public int Done { get; set; }

    /// <summary>У оценки — диалоги, которые выбранному судье нельзя (16.3)</summary>
    public int Skipped { get; set; }

    /// <summary>У оценки — сорвавшиеся на поставщике</summary>
    public int Failed { get; set; }

    public long TokensIn { get; set; }

    public long TokensCached { get; set; }

    public long TokensOut { get; set; }

    /// <summary>В валюте прайса — сумма служебного расхода запуска</summary>
    public decimal Cost { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// Отчёт сверки (15.3) — только в <c>GET Admin/Models/Checks/{id}</c>: в списке
    /// запусков он не нужен и весит десятки килобайт
    /// </summary>
    public ModelCheckReportDto? Report { get; set; }
}

/// <summary>Тело <c>POST Admin/Dialogs/Evaluations</c>: судья и размер пачки (16.3)</summary>
public class AdminEvaluationStartDto
{
    public Guid ModelId { get; set; }

    /// <summary>Не больше <c>AdminOptions.Evaluation.BatchSize</c> — потолка одного нажатия</summary>
    public int BatchSize { get; set; }
}

/// <summary>
/// Что знает экран автооценки до запуска: потолок пачки из конфигурации и последние
/// запуски. Модели берутся из справочника (<c>GET Admin/Models</c>)
/// </summary>
public class AdminEvaluationsDto
{
    public int MaxBatchSize { get; set; }

    public AdminJobDto[] Jobs { get; set; } = [];
}

/// <summary>
/// Отчёт сверки модели набором фраз (спец. admin, 15.3): доли попаданий и строка на каждую
/// фразу. Хранится этим же документом в <c>AdminJob.Report</c> и пополняется после
/// каждой фразы
/// </summary>
public class ModelCheckReportDto
{
    /// <summary>Сколько фраз в наборе — сделано столько, сколько строк в <see cref="Results"/></summary>
    public int Phrases { get; set; }

    public ModelCheckShareDto[] Shares { get; set; } = [];

    public double AverageSteps { get; set; }

    public ModelCheckPhraseDto[] Results { get; set; } = [];
}

/// <summary>
/// Доля попаданий по одному признаку. <see cref="Considered"/> — по скольким фразам
/// признак вообще считается: «без лишних слов» считают только там, где они заданы
/// </summary>
public class ModelCheckShareDto
{
    /// <summary>Признак: <c>tool</c>, <c>russian</c>, <c>clean</c>, <c>noCalls</c>, <c>failed</c></summary>
    public string Key { get; set; } = null!;

    public int Hits { get; set; }

    public int Considered { get; set; }
}

/// <summary>
/// Одна фраза: что ожидалось и что разобралось. Попадания по признаку, которого у фразы
/// нет, — <c>null</c>, а не «попал»
/// </summary>
public class ModelCheckPhraseDto
{
    public string Phrase { get; set; } = null!;

    public string? ExpectedTool { get; set; }

    public string[] Tools { get; set; } = [];

    /// <summary>Ответ модели, обрезанный: для глаз, а не для разбора</summary>
    public string Answer { get; set; } = string.Empty;

    public string? Error { get; set; }

    public int Steps { get; set; }

    public long TokensIn { get; set; }

    public long TokensOut { get; set; }

    public decimal Cost { get; set; }

    public bool ToolHit { get; set; }

    /// <summary>В ответе нет слов об устройстве приложения; <c>null</c> — у фразы их не задано</summary>
    public bool? CleanAnswer { get; set; }

    public bool RussianAnswer { get; set; }
}
