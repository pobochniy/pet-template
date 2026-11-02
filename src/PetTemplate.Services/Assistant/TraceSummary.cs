using System.Text.Json;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Шаг трассы человеческой строкой (спец. assistant, 13.2): «искал в справке: как
/// пригласить коллегу», а не JSON с аргументами.
///
/// Это объясняет результат и делает ошибку видимой — «он искал не то».
/// Глаголы берутся из словаря по имени инструмента: сочинять их модели нельзя,
/// иначе объяснение хода зависело бы от того, в каком она настроении.
///
/// Точка расширения: раздел проекта, добавляющий инструменты (7.1), дописывает
/// их глаголы в <see cref="Verbs"/>, а ключевые аргументы — в <see cref="Interesting"/>.
/// Инструмент без глагола показывается своим именем
/// </summary>
public static class TraceSummary
{
    private static readonly Dictionary<string, string> Verbs = new()
    {
        ["search_help"] = "искал в справке",
        ["ask_user"] = "задал уточняющий вопрос",
        ["apply_plan"] = "применил план",
        ["remember_fact"] = "запомнил"
    };

    /// <summary>
    /// Ключевые аргументы, которые стоит показать рядом с глаголом. Остальные молчат:
    /// человек читает трассу, чтобы понять ход мысли, а не чтобы проверить сериализацию
    /// </summary>
    private static readonly string[] Interesting = ["query", "question", "date", "text", "title", "name"];

    public static string Build(string kind, string? toolName, string arguments)
    {
        if (kind != "tool" || toolName == null) return "обращение к модели";

        var verb = Verbs.GetValueOrDefault(toolName, toolName);
        var details = Details(arguments);

        return details.Length == 0 ? verb : $"{verb}: {details}";
    }

    private static string Details(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return string.Empty;

        try
        {
            var parsed = JsonDocument.Parse(arguments).RootElement;
            if (parsed.ValueKind != JsonValueKind.Object) return string.Empty;

            var parts = parsed.EnumerateObject()
                .Where(x => Interesting.Contains(x.Name))
                .Select(x => x.Value.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            return string.Join(", ", parts);
        }
        catch (JsonException)
        {
            // Аргументы пишет модель, и годного JSON среди них бывает не всегда —
            // трасса от этого падать не должна
            return string.Empty;
        }
    }
}
