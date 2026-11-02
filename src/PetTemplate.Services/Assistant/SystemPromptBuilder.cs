using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Системный промпт хода (спец. assistant, раздел 7 «Не инструменты, а системный
/// промпт»).
///
/// Дата, время, зона, язык, название пространства и список участников с идентификаторами
/// едут готовыми: иначе модель тратит шаг на известное, а сопоставить имя с участником
/// без списка не может вовсе. Правила инструментов разделов проекта приходят от самих
/// разделов (<c>IAssistantToolSource.Instructions</c>) и встают отдельным блоком.
///
/// Память о человеке (блок «Что ты помнишь о людях», 12.5) стоит здесь же, рядом
/// со списком участников, — подписанной как данные, а не как инструкции (раздел 9).
/// </summary>
public class SystemPromptBuilder(IOptions<AssistantOptions> options, ILogger<SystemPromptBuilder> logger)
{
    /// <summary>
    /// Текст промпта лежит отдельным файлом-ресурсом: его правят как текст,
    /// а не как склейку строк
    /// </summary>
    private static readonly Lazy<string> Template = new(() => Prompts.Read("system.md"));

    public string Build(
        AssistantContext context,
        string workspaceName,
        IReadOnlyList<Profile> members,
        IReadOnlyList<AssistantFact> facts,
        IReadOnlyList<string> sections,
        AssistantPlan? pendingPlan = null,
        int questionStreak = 0,
        string? summary = null)
    {
        var local = Local(context);
        var culture = CultureInfo.GetCultureInfo("ru-RU");

        return Template.Value
            .Replace("{now}", local.ToString("dd.MM.yyyy HH:mm", culture))
            .Replace("{today}", local.ToString("dd.MM.yyyy, dddd", culture))
            .Replace("{timeZone}", context.TimeZone)
            .Replace("{language}", context.Language)
            .Replace("{workspaceName}", workspaceName)
            .Replace("{members}", Members(members))
            .Replace("{facts}", Facts(facts))
            .Replace("{sections}", Sections(sections))
            .Replace("{summary}", Summary(summary))
            .Replace("{pendingPlan}", Plan(pendingPlan, context))
            .Replace("{questionStreak}", Streak(questionStreak));
    }

    /// <summary>
    /// Местное «сейчас». Зона приезжает строкой IANA из заголовка; неизвестная строка —
    /// это UTC и запись в логах, а не отказ запроса (раздел 10)
    /// </summary>
    private DateTime Local(AssistantContext context)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZone);

            return TimeZoneInfo.ConvertTimeFromUtc(context.NowUtc, zone);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Unknown time zone '{TimeZone}', falling back to UTC", context.TimeZone);

            return context.NowUtc;
        }
    }

    /// <summary>
    /// Участники с идентификаторами: по ним модель кладёт людей в операции плана.
    /// Имени, кроме логина, у профиля нет — показываем то, что есть
    /// </summary>
    private static string Members(IReadOnlyList<Profile> members)
    {
        if (members.Count == 0) return "— в пространстве больше никого нет.";

        return string.Join("\n", members
            .OrderBy(x => x.UserName, StringComparer.Ordinal)
            .Select(x => $"- {x.UserName} (id: {x.Id})"));
    }

    /// <summary>
    /// Факты подписаны как данные, а не как инструкции (раздел 9): в системном промпте
    /// они стоят под своим заголовком со сказанным прямо «записано со слов пользователя»
    /// </summary>
    private static string Facts(IReadOnlyList<AssistantFact> facts)
    {
        if (facts.Count == 0) return "— пока ничего не записано.";

        return string.Join("\n", facts
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"- {x.Text}{(x.UserId == null ? " (про пространство)" : " (лично про собеседника)")}"));
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

    private static string Summary(string? summary) =>
        string.IsNullOrWhiteSpace(summary)
            ? string.Empty
            : $"## Резюме прежнего разговора\n\n{summary.Trim()}";

    /// <summary>
    /// Незакрытый план прикладывается кодом и в каждый ход — независимо от того, что
    /// осталось в резюме и что модель написала в прозе (12.4). Требовать от резюме
    /// «сохранить план целиком» — самое хрупкое из требований к нему, и оно снято.
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
        text.AppendLine("Операции плана (их же человек видит списком, пересказывать не надо):");
        text.AppendLine();
        text.AppendLine("```json");
        text.AppendLine(plan.Operations);
        text.AppendLine("```");

        return text.ToString();
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
    /// <summary>С третьего уточняющего вопроса подряд без движения в деле (раздел 8)</summary>
    public const int QuestionStreakLimit = 3;

    private static string Streak(int questionStreak) => questionStreak < QuestionStreakLimit
        ? string.Empty
        : $"## Внимание\n\nТы уже задал {questionStreak} уточняющих вопроса подряд, и дело " +
          "с места не сдвинулось. Больше не спрашивай: покажи план из того, что понял, " +
          "или скажи прямо, что не разобрал просьбу.";
}
