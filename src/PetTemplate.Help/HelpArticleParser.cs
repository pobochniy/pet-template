namespace PetTemplate.Help;

/// <summary>
/// Разбор статьи справки (спец. help, 3). Формат нарочно узкий: фронтматтер
/// с <c>title</c> и необязательным <c>page</c>, дальше только разделы <c>##</c>.
/// Чего формат не знает, разбор не угадывает, а складывает в
/// <see cref="HelpArticle.Problems"/> — решать, ошибка ли это, будет линтер
/// </summary>
public static class HelpArticleParser
{
    /// <summary>Строка с формулировками вопроса в конце раздела (3.2)</summary>
    public const string AsksPrefix = "Как спрашивают:";

    private const string FrontMatterFence = "---";

    public static HelpArticle Parse(string name, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var problems = new List<string>();
        var position = 0;

        var (title, page) = FrontMatter(lines, ref position, problems);

        var sections = new List<HelpSection>();
        string? sectionTitle = null;
        var sectionLine = 0;
        var body = new List<string>();

        for (; position < lines.Length; position++)
        {
            var line = lines[position];
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (sectionTitle != null) sections.Add(Section(sectionTitle, sectionLine, body));
                sectionTitle = line[3..].Trim();
                sectionLine = position + 1;
                body.Clear();
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                problems.Add($"строка {position + 1}: заголовок первого уровня — название статьи живёт в title");
                continue;
            }

            if (sectionTitle == null)
            {
                if (line.Trim().Length > 0)
                    problems.Add($"строка {position + 1}: текст до первого раздела не попадёт в поиск");
                continue;
            }

            body.Add(line);
        }

        if (sectionTitle != null) sections.Add(Section(sectionTitle, sectionLine, body));

        return new HelpArticle(name, title ?? name, page, sections, problems);
    }

    /// <summary>
    /// Грубая оценка токенов — та же, что у нарезки поиска (символов на токен четыре):
    /// границы раздела и статьи (3.3) должны значить одно и то же для линтера и индекса
    /// </summary>
    public static int Tokens(string text) => (text.Length + 3) / 4;

    private static (string? Title, string? Page) FrontMatter(string[] lines, ref int position, List<string> problems)
    {
        if (lines.Length == 0 || lines[0].Trim() != FrontMatterFence)
        {
            problems.Add("нет фронтматтера с title");

            return (null, null);
        }

        string? title = null, page = null;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line == FrontMatterFence)
            {
                position = i + 1;
                if (title == null) problems.Add("во фронтматтере нет title");

                return (title, page);
            }

            if (line.Length == 0) continue;

            var colon = line.IndexOf(':');
            var key = colon < 0 ? line : line[..colon].Trim();
            var value = colon < 0 ? "" : line[(colon + 1)..].Trim().Trim('"', '\'');
            switch (key)
            {
                case "title":
                    title = value;
                    break;
                case "page":
                    page = value;
                    break;
                default:
                    problems.Add($"строка {i + 1}: неизвестное поле фронтматтера «{key}»");
                    break;
            }
        }

        position = lines.Length;
        problems.Add("фронтматтер не закрыт");

        return (title, page);
    }

    private static HelpSection Section(string title, int line, List<string> body)
    {
        // «Как спрашивают:» — последняя непустая строка раздела; где-то в середине
        // она уже не формулировки вопроса, а часть ответа, и линтер скажет, что её нет
        var lines = body.ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);

        string? asks = null;
        if (lines.Count > 0 && lines[^1].TrimStart().StartsWith(AsksPrefix, StringComparison.Ordinal))
        {
            asks = lines[^1].Trim()[AsksPrefix.Length..].Trim();
            lines.RemoveAt(lines.Count - 1);
        }

        return new HelpSection(title, string.Join('\n', lines).Trim(), asks, line);
    }
}
