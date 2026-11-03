using PetTemplate.Shared.Dto.Admin;

namespace PetTemplate.Services.Assistant.Check;

/// <summary>
/// Итоговая таблица сверки (спец. admin, 15.3): доли попаданий, шаги и строки по фразам.
/// Она отвечает на единственный вопрос, который имеет значение при смене модели, —
/// «стало лучше или хуже»
/// </summary>
public static class ModelCheckReport
{
    /// <summary>Признаки в порядке таблицы; подписи к ним — у того, кто печатает</summary>
    public static readonly IReadOnlyList<string> Keys = ["tool", "russian", "clean", "noCalls", "failed"];

    /// <param name="phrases">Сколько фраз в наборе — у незаконченной сверки строк меньше</param>
    /// <param name="results">Сделанные фразы</param>
    public static ModelCheckReportDto Build(int phrases, IReadOnlyCollection<ModelCheckPhraseDto> results) => new()
    {
        Phrases = phrases,
        Results = [.. results],
        AverageSteps = results.Count > 0 ? results.Average(x => x.Steps) : 0,
        Shares =
        [
            Share("tool", results, x => x.ToolHit),
            Share("russian", results, x => x.RussianAnswer),
            Share("clean", results, x => x.CleanAnswer),
            Share("noCalls", results, x => x.Tools.Length == 0),
            Share("failed", results, x => x.Error != null)
        ]
    };

    /// <summary>Признак, которого у фразы нет (<c>null</c>), в долю не идёт вовсе</summary>
    private static ModelCheckShareDto Share(
        string key,
        IReadOnlyCollection<ModelCheckPhraseDto> results,
        Func<ModelCheckPhraseDto, bool?> hit)
    {
        var considered = results.Select(hit).Where(x => x != null).ToList();

        return new ModelCheckShareDto
        {
            Key = key,
            Hits = considered.Count(x => x == true),
            Considered = considered.Count
        };
    }
}
