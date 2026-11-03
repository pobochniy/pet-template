using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using PetTemplate.Services.Assistant.Sections;

namespace PetTemplate.Services.Assistant.Check;

/// <summary>
/// Фраза набора сверки и то, что от неё ожидается (спец. admin, 15.3).
///
/// Ожидания намеренно мягкие: сверяется не дословный ответ, а то, что модель
/// разобрала — какой инструмент позвала и не рассказала ли лишнего.
///
/// Набор ядра — ресурс сборки (<c>phrases.json</c> рядом): цикл, справка, память,
/// уточняющий вопрос. Раздел проекта со своими инструментами (спец. assistant, 7.1)
/// приносит свои фразы и данные под них сам (<c>IAssistantSection.Phrases</c> и <c>Seed</c>)
/// </summary>
public class ModelCheckPhrase
{
    public string Phrase { get; set; } = string.Empty;

    /// <summary>Инструмент, который должен быть позван хотя бы раз</summary>
    public string? Tool { get; set; }

    /// <summary>
    /// Чего не должно быть в ответе — слова об устройстве приложения (спец. help, 4):
    /// модели, серверы, базы. Смысловой пересказ без этих слов ловит только человек,
    /// читая ответ в отчёте
    /// </summary>
    public string[]? Forbidden { get; set; }

    private static readonly Lazy<IReadOnlyList<ModelCheckPhrase>> Set = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("Assistant.Check.phrases.json", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;

        return JsonSerializer.Deserialize<List<ModelCheckPhrase>>(stream, Json) ?? [];
    });

    /// <summary>Набор ядра — тот, что лежит в сборке</summary>
    public static IReadOnlyList<ModelCheckPhrase> Embedded() => Set.Value;

    /// <summary>Набор сверки целиком: фразы ядра, за ними — фразы разделов по порядку регистрации</summary>
    public static IReadOnlyList<ModelCheckPhrase> Of(IEnumerable<IAssistantSection> sections) =>
        [.. Embedded(), .. sections.SelectMany(x => x.Phrases)];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
