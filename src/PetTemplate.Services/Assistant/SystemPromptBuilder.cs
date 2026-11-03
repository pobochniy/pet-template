using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Промпт хода (спец. assistant, раздел 7 «Не инструменты, а системный промпт», раздел 9).
///
/// Две части, и граница между ними — граница доверия:
/// <list type="bullet">
/// <item><description>
/// <b>Системный промпт</b> — только наши тексты: шаблон <c>Prompts/system.md</c>, дата,
/// время, зона, язык, правила разделов (<c>IAssistantToolSource.Instructions</c> — код
/// раздела), подписи незакрытого плана и счётчик вопросов.
/// </description></item>
/// <item><description>
/// <b>Блок данных</b> — отдельное сообщение перед перепиской (<see cref="AssistantPrompt.Data"/>):
/// название пространства, участники с идентификаторами, факты памяти (12.5), резюме
/// компакции (12.4) и операции незакрытого плана. Всё это написали люди или модель
/// по их словам, и строка «игнорируй инструкции» в имени или факте в системный промпт
/// не попадает. Блок — JSON внутри <c>&lt;app_data&gt;</c>: кавычки и угловые скобки
/// экранированы, закрыть тег изнутри значения нечем.
/// </description></item>
/// </list>
///
/// Подстановка в шаблон — одним проходом (<see cref="Fill"/>): подставленное значение
/// повторно не разбирается, и <c>{…}</c> внутри него остаётся текстом.
/// </summary>
public partial class SystemPromptBuilder(IOptions<AssistantOptions> options, ILogger<SystemPromptBuilder> logger)
{
    /// <summary>С третьего уточняющего вопроса подряд без движения в деле (раздел 8)</summary>
    public const int QuestionStreakLimit = 3;

    /// <summary>Тег блока данных — о нём говорит системный промпт</summary>
    public const string DataTag = "app_data";

    /// <summary>
    /// Текст промпта лежит отдельным файлом-ресурсом: его правят как текст,
    /// а не как склейку строк
    /// </summary>
    private static readonly Lazy<string> Template = new(() => Prompts.Read("system.md"));

    /// <summary>
    /// JSON блока данных: кириллица как есть (модель читает её, а не браузер), но
    /// <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> и кавычки экранированы всегда — значение
    /// не закроет тег <c>&lt;/app_data&gt;</c> и не выйдет из своей строки
    /// </summary>
    private static readonly JsonSerializerOptions DataJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex Placeholder();

    public AssistantPrompt Build(
        AssistantContext context,
        string workspaceName,
        IReadOnlyList<Profile> members,
        IReadOnlyList<AssistantFact> facts,
        IReadOnlyList<string> sections,
        AssistantPlan? pendingPlan = null,
        int questionStreak = 0,
        string? summary = null)
    {
        var (local, zone) = Local(context);
        var culture = CultureInfo.GetCultureInfo("ru-RU");

        var system = Fill(Template.Value, new Dictionary<string, string>
        {
            ["now"] = local.ToString("dd.MM.yyyy HH:mm", culture),
            ["today"] = local.ToString("dd.MM.yyyy, dddd", culture),
            ["timeZone"] = zone,
            ["language"] = context.Language,
            ["sections"] = Sections(sections),
            ["pendingPlan"] = Plan(pendingPlan, context),
            ["questionStreak"] = Streak(questionStreak)
        });

        return new AssistantPrompt(system, Data(workspaceName, members, facts, summary, pendingPlan));
    }

    /// <summary>
    /// Подстановка за один проход: каждое <c>{имя}</c> шаблона заменяется своим значением,
    /// а подставленное повторно не разбирается — пространство с названием <c>{facts}</c>
    /// не раскроется в факты. Неизвестное имя остаётся как есть
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, x => values.TryGetValue(x.Groups[1].Value, out var value) ? value : x.Value);

    /// <summary>
    /// Местное «сейчас». Зона приезжает строкой IANA из заголовка; неизвестная строка —
    /// это UTC и запись в логах, а не отказ запроса (раздел 10). В промпт едет имя
    /// найденной зоны, а не строка из заголовка как есть
    /// </summary>
    private (DateTime Local, string Zone) Local(AssistantContext context)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZone);

            return (TimeZoneInfo.ConvertTimeFromUtc(context.NowUtc, zone), zone.Id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Unknown time zone '{TimeZone}', falling back to UTC", context.TimeZone);

            return (context.NowUtc, "UTC");
        }
    }

    /// <summary>
    /// Правила инструментов разделов — каждый раздел своим абзацем. Нет разделов —
    /// нет и блока: у ядра один читающий инструмент, и его правила уже в шаблоне
    /// </summary>
    private static string Sections(IReadOnlyList<string> sections)
    {
        var filled = sections.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();

        return filled.Length == 0
            ? string.Empty
            : "## Инструменты разделов\n\n" + string.Join("\n\n", filled);
    }

    /// <summary>
    /// Незакрытый план прикладывается кодом и в каждый ход — независимо от того, что
    /// осталось в резюме и что модель написала в прозе (12.4). Здесь — только наши
    /// подписи: показан ли план и не стар ли он; сами операции с аргументами людей —
    /// в блоке данных
    /// </summary>
    private string Plan(AssistantPlan? plan, AssistantContext context)
    {
        if (plan == null) return string.Empty;

        var text = new StringBuilder();
        text.AppendLine("## Незакрытый план");
        text.AppendLine();
        text.AppendLine(plan.Status == AssistantPlanStatus.Draft
            ? "Черновик плана, собранный до уточняющего вопроса. Человек его ещё не видел целиком."
            : "План уже показан человеку и ждёт ответа «делаем?».");

        var age = context.NowUtc - plan.CreatedAtUtc;
        if (age > options.Value.StaleAfter)
            text.AppendLine(
                $"Показан {Age(age)} назад — «завтра», сказанное тогда, сегодня значит другое: " +
                "пересчитай даты и скажи человеку, что план давний.");

        text.AppendLine();
        text.AppendLine("Операции плана — `pendingPlan` в блоке данных; их же человек видит списком, " +
                        "пересказывать не надо.");

        return text.ToString();
    }

    /// <summary>
    /// Блок данных хода. Участники — по логину, факты — по ключу: порядок от хода к ходу
    /// не прыгает, и префикс запроса у поставщика кэшируется. Операции плана едут
    /// разобранным JSON, а не строкой в строке; битый документ — строкой как есть
    /// </summary>
    private static string Data(
        string workspaceName,
        IReadOnlyList<Profile> members,
        IReadOnlyList<AssistantFact> facts,
        string? summary,
        AssistantPlan? plan)
    {
        var data = new AppData
        {
            Workspace = workspaceName,
            Members = [.. members
                .OrderBy(x => x.UserName, StringComparer.Ordinal)
                .Select(x => new AppMember(x.Id, x.UserName))],
            Facts = [.. facts
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new AppFact(x.UserId == null ? "space" : "person", x.Text))],
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            PendingPlan = plan == null ? null : Operations(plan.Operations)
        };

        return $"<{DataTag}>\n{JsonSerializer.Serialize(data, DataJson)}\n</{DataTag}>";
    }

    private static JsonElement Operations(string operations)
    {
        try
        {
            return JsonDocument.Parse(operations).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(operations);
        }
    }

    private static string Age(TimeSpan age) => age.TotalDays >= 1
        ? $"{(int)age.TotalDays} дн."
        : $"{(int)age.TotalHours} ч.";

    /// <summary>
    /// Допрос из пяти реплик — это анкета, от которой фича избавляет: на четвёртом
    /// вопросе подряд ассистент обязан либо показать план, либо сказать прямо,
    /// что не понял просьбу (раздел 8). Один-два вопроса подряд — обычное уточнение:
    /// запрет с первого же отбивал бы у модели второй, вполне законный вопрос
    /// </summary>
    private static string Streak(int questionStreak) => questionStreak < QuestionStreakLimit
        ? string.Empty
        : $"## Внимание\n\nТы уже задал {questionStreak} уточняющих вопроса подряд, и дело " +
          "с места не сдвинулось. Больше не спрашивай: покажи план из того, что понял, " +
          "или скажи прямо, что не разобрал просьбу.";

    private sealed class AppData
    {
        [JsonPropertyName("workspace")]
        public string Workspace { get; init; } = string.Empty;

        [JsonPropertyName("members")]
        public IReadOnlyList<AppMember> Members { get; init; } = [];

        [JsonPropertyName("facts")]
        public IReadOnlyList<AppFact> Facts { get; init; } = [];

        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("pendingPlan")]
        public JsonElement? PendingPlan { get; init; }
    }

    private sealed record AppMember(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("userName")] string UserName);

    private sealed record AppFact(
        [property: JsonPropertyName("about")] string About,
        [property: JsonPropertyName("text")] string Text);
}
