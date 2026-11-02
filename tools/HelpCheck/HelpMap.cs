using System.Text;
using YamlDotNet.RepresentationModel;

namespace PetTemplate.HelpCheck;

/// <summary>
/// Запись карты о разделе спецификации (спец. help, 5.1): раздел описан в статье
/// (<see cref="Article"/> + <see cref="Hash"/>), осознанно не для справки
/// (<see cref="Skip"/>) или ещё не разнесён (<see cref="Todo"/> — заготовка
/// <c>--init</c>, карту она не проходит)
/// </summary>
public record MapEntry(string? Article, string? Hash, bool Skip, bool Todo)
{
    public static readonly MapEntry SkipEntry = new(null, null, true, false);

    public static readonly MapEntry TodoEntry = new(null, null, false, true);
}

/// <summary>Карта целиком: файл спецификации → «skip» целиком или разделы по ключам</summary>
public class MapDocument
{
    /// <summary>Порядок файлов — порядок в карте; <c>null</c> — файл целиком <c>skip</c></summary>
    public List<(string File, List<(string Key, MapEntry Entry)>? Sections)> Files { get; } = [];
}

/// <summary>
/// Карта справки и её сверка со спецификациями (спец. help, 5.1–5.2). Сверка —
/// только в ПР: устаревшая справка безопасна, она проверенный текст, просто неполный;
/// опасна непроверенная, а её ловит линтер
/// </summary>
public static class HelpMap
{
    private const string SkipValue = "skip";

    private const string TodoValue = "todo";

    public static List<string> Check(string root)
    {
        var errors = new List<string>();
        var map = Load(root, errors);
        if (map == null) return errors;

        var help = Path.Combine(root, Repository.HelpDirectory);
        var specs = SpecFiles(root);

        foreach (var (file, sections) in map.Files)
        {
            if (!specs.ContainsKey(file))
            {
                errors.Add($"{file}: в карте есть, а спецификации нет — переименовали или удалили");
                continue;
            }

            if (sections == null) continue;

            var units = specs[file];
            foreach (var duplicate in units.GroupBy(x => x.Key).Where(x => x.Count() > 1))
                errors.Add($"{file} › {duplicate.Key}: два раздела с одним заголовком — карта их не различит, переименуй один");

            var byKey = units.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.First());
            foreach (var (key, entry) in sections)
            {
                if (!byKey.TryGetValue(key, out var unit))
                {
                    errors.Add($"{file} › {key}: в карте есть, а в спецификации такого раздела больше нет");
                    continue;
                }

                if (entry.Todo)
                    errors.Add($"{file} › {key}: не разнесён — отнеси к статье или пометь skip");
                else if (entry.Article != null && !File.Exists(Path.Combine(help, entry.Article + ".md")))
                    errors.Add($"{file} › {key}: статьи {entry.Article} нет в {Repository.HelpDirectory}/");
                else if (entry.Article != null && entry.Hash != unit.Hash)
                    errors.Add($"{file} › {key}: раздел поменялся, а статью {entry.Article} с ним не сверили " +
                               $"(после сверки — map --rehash {file} \"{key}\")");
            }

            var known = sections.Select(x => x.Key).ToHashSet();
            foreach (var unit in units.Where(x => !known.Contains(x.Key)))
                errors.Add($"{file} › {unit.Key}: раздела нет в карте — отнеси к статье или пометь skip");
        }

        var mapped = map.Files.Select(x => x.File).ToHashSet();
        foreach (var file in specs.Keys.Where(x => !mapped.Contains(x)))
            errors.Add($"{file}: спецификации нет в карте (map --init {file})");

        return errors;
    }

    /// <summary>
    /// Хэши считает утилита, а не модель (5.1): после сверки статьи с разделом —
    /// записать хэш раздела. Без <paramref name="section"/> — все разделы файла со статьёй
    /// </summary>
    public static List<string> Rehash(string root, string file, string? section)
    {
        var errors = new List<string>();
        var map = Load(root, errors);
        if (map == null) return errors;

        var units = SpecFiles(root).GetValueOrDefault(file);
        var index = map.Files.FindIndex(x => x.File == file);
        if (units == null || index < 0 || map.Files[index].Sections is not { } sections)
            return [$"{file}: нет такой спецификации с разделами в карте"];

        var byKey = units.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.First());
        var touched = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var (key, entry) = sections[i];
            if (section != null && key != section) continue;
            if (entry.Article == null || !byKey.TryGetValue(key, out var unit)) continue;

            sections[i] = (key, entry with { Hash = unit.Hash });
            touched++;
        }

        if (section != null && touched == 0)
            return [$"{file} › {section}: в карте нет такого раздела со статьёй"];

        Save(root, map, SpecFiles(root));
        Console.WriteLine($"{file}: хэшей обновлено — {touched}");

        return [];
    }

    /// <summary>
    /// Дописать в карту разделы спецификации, которых в ней нет, заготовкой <c>todo</c>:
    /// заголовки набирает утилита, а не модель — опечатка в ключе стала бы новым
    /// «пропавшим» разделом
    /// </summary>
    public static List<string> Init(string root, string file)
    {
        var errors = new List<string>();
        var map = File.Exists(MapPath(root)) ? Load(root, errors) : new MapDocument();
        if (map == null) return errors;

        var specs = SpecFiles(root);
        if (!specs.TryGetValue(file, out var units)) return [$"{file}: нет такой спецификации в {Repository.SpecsDirectory}/"];

        var index = map.Files.FindIndex(x => x.File == file);
        if (index < 0)
        {
            map.Files.Add((file, []));
            index = map.Files.Count - 1;
        }

        if (map.Files[index].Sections is not { } sections) return [$"{file}: в карте целиком skip"];

        var known = sections.Select(x => x.Key).ToHashSet();
        var added = units.Where(x => known.Add(x.Key)).Select(x => (x.Key, MapEntry.TodoEntry)).ToList();
        sections.AddRange(added);

        Save(root, map, specs);
        Console.WriteLine($"{file}: заготовок дописано — {added.Count}");

        return [];
    }

    public static string MapPath(string root) => Path.Combine(root, Repository.HelpDirectory, Repository.MapFile);

    /// <summary>
    /// <c>specs/*.md</c> верхнего уровня: вложенные каталоги, если появятся, — не спецификации
    /// и в карту не входят (5.1)
    /// </summary>
    public static Dictionary<string, List<SpecUnit>> SpecFiles(string root) =>
        Directory.GetFiles(Path.Combine(root, Repository.SpecsDirectory), "*.md")
            .ToDictionary(x => Path.GetFileName(x), x => SpecOutline.Units(File.ReadAllText(x)));

    public static MapDocument? Load(string root, List<string> errors)
    {
        var path = MapPath(root);
        if (!File.Exists(path))
        {
            errors.Add($"{Repository.HelpDirectory}/{Repository.MapFile}: карты нет");

            return null;
        }

        var yaml = new YamlStream();
        try
        {
            using var reader = new StringReader(File.ReadAllText(path));
            yaml.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            errors.Add($"{Repository.HelpDirectory}/{Repository.MapFile}: не разбирается — {e.Message}");

            return null;
        }

        var map = new MapDocument();
        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode files) return map;

        foreach (var (fileNode, value) in files.Children)
        {
            var file = ((YamlScalarNode)fileNode).Value!;
            if (value is YamlScalarNode { Value: SkipValue })
            {
                map.Files.Add((file, null));
                continue;
            }

            if (value is not YamlMappingNode sectionNodes)
            {
                errors.Add($"{file}: ждём skip или разделы");
                continue;
            }

            var sections = new List<(string, MapEntry)>();
            foreach (var (keyNode, entryNode) in sectionNodes.Children)
            {
                var key = ((YamlScalarNode)keyNode).Value!;
                var entry = Entry(entryNode);
                if (entry == null) errors.Add($"{file} › {key}: ждём skip, todo или {{ article, hash }}");
                else sections.Add((key, entry));
            }

            map.Files.Add((file, sections));
        }

        return errors.Count == 0 ? map : null;
    }

    private static MapEntry? Entry(YamlNode node) => node switch
    {
        YamlScalarNode { Value: SkipValue } => MapEntry.SkipEntry,
        YamlScalarNode { Value: TodoValue } => MapEntry.TodoEntry,
        YamlMappingNode mapping when Scalar(mapping, "article") is { Length: > 0 } article =>
            new MapEntry(article, Scalar(mapping, "hash"), false, false),
        _ => null
    };

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? (value as YamlScalarNode)?.Value : null;

    /// <summary>
    /// Карту пишем сами, а не сериализатором: файлы по имени, разделы —
    /// в порядке оглавления, пропавшие — в конце (их видно сверке). Так диф карты в ПР
    /// показывает ровно то, что поменялось
    /// </summary>
    private static void Save(string root, MapDocument map, Dictionary<string, List<SpecUnit>> specs)
    {
        var text = new StringBuilder();
        text.AppendLine("# Карта справки: раздел спецификации → статья help/ или skip (specs/help.md, 5.1).");
        text.AppendLine("# Хэши считает HelpCheck (map --rehash), заготовки дописывает map --init.");

        foreach (var (file, sections) in map.Files.OrderBy(x => x.File, StringComparer.Ordinal))
        {
            text.AppendLine();
            if (sections == null)
            {
                text.AppendLine($"{file}: {SkipValue}");
                continue;
            }

            text.AppendLine($"{file}:");
            var order = (specs.GetValueOrDefault(file) ?? [])
                .Select((x, i) => (x.Key, i))
                .GroupBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.First().i);

            foreach (var (key, entry) in sections.OrderBy(x => order.GetValueOrDefault(x.Key, int.MaxValue)))
            {
                var value = entry switch
                {
                    { Skip: true } => SkipValue,
                    { Todo: true } => TodoValue,
                    _ => $"{{ article: {entry.Article}, hash: {entry.Hash ?? "\"\""} }}"
                };
                text.AppendLine($"  {Quote(key)}: {value}");
            }
        }

        File.WriteAllText(MapPath(root), text.ToString());
    }

    private static string Quote(string key) => "\"" + key.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
