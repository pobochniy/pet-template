using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PetTemplate.HelpCheck;

/// <summary>Единица карты: лист оглавления спецификации и его текст</summary>
/// <param name="Key">Заголовок без номера — номера съезжают при каждой вставке, заголовок нет</param>
public record SpecUnit(string Key, string Text)
{
    public string Hash => SpecOutline.Hash(Text);
}

/// <summary>
/// Оглавление спецификации для карты (спец. help, 5.1). Единица — лист:
/// <c>##</c> без <c>###</c> внутри или <c>###</c>; <c>####</c> принадлежит своему
/// <c>###</c>.
///
/// Вступление <c>##</c> до первого <c>###</c> — тоже единица, под заголовком самого
/// <c>##</c>: иначе правка такого абзаца прошла бы мимо карты, а гарантия «смержили
/// фичу — справка о ней подумала» держится как раз на том, что текста вне карты нет.
/// Шапка файла до первого <c>##</c> (название и статус) в карту не входит.
///
/// Одинаковые заголовки в одном файле («Основные свойства» у двух сущностей)
/// различаются родителем: «Страница › Основные свойства»
/// </summary>
public static partial class SpecOutline
{
    public const string ParentSeparator = " › ";

    public static List<SpecUnit> Units(string markdown)
    {
        var raw = new List<(string Parent, string Title, StringBuilder Text)>();
        string? parent = null;
        var fence = false;

        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fence = !fence;

            var heading = fence ? null : HeadingRegex().Match(line);
            if (heading is { Success: true } && heading.Groups[1].Length is 2 or 3)
            {
                var title = Title(heading.Groups[2].Value);
                if (heading.Groups[1].Length == 2) parent = title;
                raw.Add((parent ?? title, title, new StringBuilder()));
                continue;
            }

            if (raw.Count > 0) raw[^1].Text.AppendLine(line);
        }

        // ## с ### внутри остаётся единицей только своим вступлением: пустое — не единица
        var units = raw
            .Where((x, i) => !(x.Parent == x.Title && i + 1 < raw.Count && raw[i + 1].Parent == x.Title
                               && raw[i + 1].Title != x.Title && Normalize(x.Text.ToString()).Length == 0))
            .ToList();

        var duplicates = units.GroupBy(x => x.Title).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();

        return
        [
            .. units.Select(x => new SpecUnit(
                duplicates.Contains(x.Title) && x.Parent != x.Title ? x.Parent + ParentSeparator + x.Title : x.Title,
                x.Text.ToString()))
        ];
    }

    /// <summary>
    /// Хэш нормализованного текста: пробелы и переводы строк схлопнуты. Строки заголовка
    /// в тексте нет — перенумерация раздела хэш не меняет
    /// </summary>
    public static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text))))[..16];

    private static string Normalize(string text) => WhitespaceRegex().Replace(text, " ").Trim();

    /// <summary>«5.3 Курс», «5.3. Курс», «12. Тесты» → «Курс», «Тесты»</summary>
    private static string Title(string heading) => NumberRegex().Replace(heading.Trim(), "");

    [GeneratedRegex(@"^(#{2,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\d+(\.\d+)*\.?\s+")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
