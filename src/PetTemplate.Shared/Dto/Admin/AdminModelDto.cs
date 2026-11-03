namespace PetTemplate.Shared.Dto.Admin;

/// <summary>
/// Ответ <c>GET Admin/Models</c> (спец. admin, 15.1): справочник по порядку, валюта прайса
/// и модель распознавания голоса — она не строка справочника, но тоже стоит денег
/// </summary>
public class AdminModelsDto
{
    /// <summary>Валюта прайса — подпись, курсов нет (<c>SpendOptions.Currency</c>)</summary>
    public string Currency { get; set; } = string.Empty;

    public AdminModelDto[] Models { get; set; } = [];

    /// <summary>
    /// Модель расшифровки голоса из конфигурации (спец. assistant, 6.2): правится
    /// не здесь, а переменной окружения, и цена у неё — за минуту. <c>null</c> — голос выключен
    /// </summary>
    public AdminTranscriptionModelDto? Transcription { get; set; }
}

/// <summary>Строка справочника моделей — цены по прайсу поставщика за 1000 токенов</summary>
public class AdminModelDto
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = null!;

    public string Key { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public int Priority { get; set; }

    public decimal CostIn { get; set; }

    public decimal CostCached { get; set; }

    public decimal CostOut { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>
    /// Своя видеокарта (спец. admin, 16.3): такому судье можно любой диалог, и экран
    /// автооценки предлагает его первым
    /// </summary>
    public bool IsLocal { get; set; }

    /// <summary>
    /// До какого момента модель разомкнута (спец. assistant, 16.2); <c>null</c> — замкнута.
    /// Срок в прошлом — пауза вышла, и следующий ход станет пробным
    /// </summary>
    public DateTime? OpenUntilUtc { get; set; }

    /// <summary>Отказы подряд: на <c>FailuresToOpen</c> модель размыкается</summary>
    public int FailureCount { get; set; }

    /// <summary>По этой модели уже ходили — поставщика и ключ не поменять (15.1)</summary>
    public bool IsUsed { get; set; }

    public DateTime UpdatedUtc { get; set; }

    /// <summary>Кто правил последним; <c>null</c> у строки, заведённой системой</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Токен конкуренции — <c>xmin</c> строки. Едет обратно в правке: строку, которую
    /// за это время поправили, правка не перезапишет (15.1)
    /// </summary>
    public uint Version { get; set; }
}

/// <summary>Модель расшифровки голоса — строка из конфигурации, а не справочника</summary>
public class AdminTranscriptionModelDto
{
    public string Provider { get; set; } = null!;

    public string Model { get; set; } = null!;

    /// <summary><c>Transcription.CostPerMinute</c> — в валюте прайса</summary>
    public decimal CostPerMinute { get; set; }
}

/// <summary>
/// Тело <c>POST Admin/Models</c> и <c>PUT Admin/Models/{id}</c>. Порядка здесь нет:
/// новая модель встаёт последней, а порядок меняется целиком отдельным методом (15.2)
/// </summary>
public class AdminModelEditDto
{
    public string Provider { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public decimal CostIn { get; set; }

    public decimal CostCached { get; set; }

    public decimal CostOut { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>Своя видеокарта — годится в судьи для любого диалога (16.3)</summary>
    public bool IsLocal { get; set; }

    /// <summary>
    /// <see cref="AdminModelDto.Version"/> строки, которую правят; у новой не читается.
    /// Не совпало — правку отклоняет сервер: строку уже поправили
    /// </summary>
    public uint Version { get; set; }
}
