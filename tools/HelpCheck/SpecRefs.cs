using System.Text.RegularExpressions;

namespace PetTemplate.HelpCheck;

/// <summary>Ссылка на раздел, которого в спецификации нет</summary>
public record RefError(string File, int Line, string Reference, string Reason)
{
    public override string ToString() => $"{File}:{Line}: «{Reference}» — {Reason}";
}

/// <summary>
/// Ссылки на спецификации из кода и документов (спец. infrastructure, 5): комментарий
/// «спец. admin, 3.5» или «<c>specs/admin.md</c>, 3.5» живёт дольше раздела, на который
/// показывает, — раздел перенумеровали или убрали, а ссылка ведёт в никуда. Проверка
/// находит такие ссылки в <c>*.cs</c>, <c>*.ts</c>, <c>*.yml</c>, <c>*.md</c> и соседях
/// и сверяет номер с заголовками <c>specs/*.md</c>.
///
/// Формы: «спец. имя, N.M», «спец. имя, раздел N», «спец. имя, N и M»,
/// «<c>specs/имя.md</c>, N.M» и «<c>specs/имя.md</c> (разделы N и M)». Ссылка без номера
/// проверяется только на то, что файл есть. Ссылки внутри одной спецификации на свои
/// разделы («раздел 13») не проверяются: имени файла в них нет
/// </summary>
public static partial class SpecRefs
{
    private static readonly string[] Extensions =
        [".cs", ".ts", ".yml", ".yaml", ".md", ".sh", ".conf", ".mjs", ".html"];

    private static readonly string[] SkippedDirectories =
        ["node_modules", "bin", "obj", ".git", "dist", ".angular", "out", "worktrees", ".artifacts-tests", "plan"];

    public static List<RefError> Check(string root)
    {
        var specs = Path.Combine(root, Repository.SpecsDirectory);
        var sections = Directory.Exists(specs)
            ? Directory.GetFiles(specs, "*.md").ToDictionary(
                x => Path.GetFileNameWithoutExtension(x),
                x => Sections(File.ReadAllText(x)))
            : [];

        return
        [
            .. Files(root)
                .Order(StringComparer.Ordinal)
                .SelectMany(file => Check(Path.GetRelativePath(root, file).Replace('\\', '/'),
                    File.ReadAllText(file), sections))
        ];
    }

    public static IEnumerable<RefError> Check(string file, string text, IReadOnlyDictionary<string, HashSet<string>> sections)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in ReferenceRegex().Matches(lines[i]))
            {
                var spec = match.Groups["spec"].Success ? match.Groups["spec"].Value : match.Groups["path"].Value;
                if (!sections.TryGetValue(spec, out var known))
                {
                    yield return new RefError(file, i + 1, match.Value.Trim(), $"нет спецификации specs/{spec}.md");
                    continue;
                }

                foreach (var number in match.Groups["n"].Captures.Select(x => x.Value).Where(x => !known.Contains(x)))
                    yield return new RefError(file, i + 1, match.Value.Trim(), $"в specs/{spec}.md нет раздела {number}");
            }
        }
    }

    /// <summary>Номера заголовков <c>##</c>–<c>####</c>: «5.3 Курс», «12. Тесты» → 5.3, 12</summary>
    public static HashSet<string> Sections(string markdown)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var fence = false;

        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fence = !fence;
            if (fence) continue;

            var heading = HeadingRegex().Match(line);
            if (heading.Success) result.Add(heading.Groups[1].Value);
        }

        return result;
    }

    private static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.GetDirectories(dir).Where(x => !SkippedDirectories.Contains(Path.GetFileName(x))))
                pending.Push(sub);

            foreach (var file in Directory.GetFiles(dir)
                         .Where(x => Extensions.Contains(Path.GetExtension(x)) || Path.GetFileName(x) == "Dockerfile"))
                yield return file;
        }
    }

    // «спец. admin, 3.5», «спец. accounts, раздел 10», «спец. admin, 15.3 и 16.3», «спец. search»;
    // «specs/admin.md, 3.3», «`specs/admin.md` (разделы 3.3 и 7)». Номер — только после запятой
    // или «(раздел…»: «спец. admin 2024» номером не считается
    [GeneratedRegex(
        @"(?:спец\.\s+(?<spec>[a-z][a-z-]*)|`?specs/(?<path>[a-z][a-z-]*)\.md`?)" +
        @"(?:(?:,\s*|\s*\()(?:разделы?\s+)?(?<n>\d+(?:\.\d+)*)(?:\s+и\s+(?<n>\d+(?:\.\d+)*))*)?")]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex(@"^#{2,4}\s+(\d+(?:\.\d+)*)\.?\s")]
    private static partial Regex HeadingRegex();
}
