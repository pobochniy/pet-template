using System.Text.RegularExpressions;
using PetTemplate.Help;

namespace PetTemplate.HelpCheck;

/// <summary>Ошибка линтера: «файл, раздел, что не так» (спец. help, 4.2)</summary>
public record LintError(string File, string? Section, string Message)
{
    public override string ToString() =>
        Section == null ? $"{File}: {Message}" : $"{File} › {Section}: {Message}";
}

/// <summary>
/// Линтер справки (спец. help, 4.2). Второй слой, а не первый: ловит то, что
/// просочилось механически — забытое имя поля, скопированный абзац. Смысловую утечку
/// без единого стоп-слова ловит только человек на ревью.
///
/// Всё здесь — ошибки, предупреждений нет: справку с ошибкой ассистент перескажет
/// кому угодно, и «потом поправим» для неё не годится
/// </summary>
public static partial class HelpLinter
{
    /// <summary>Потолок раздела в токенах — один раздел, один чанк (3.3)</summary>
    public const int SectionTokens = 350;

    /// <summary>Потолок статьи в токенах (3.3)</summary>
    public const int ArticleTokens = 4_000;

    /// <summary>
    /// Стоп-слова: инфраструктура, механизмы, поставщики. Основы, а не слова целиком —
    /// «вебхук» ловит и «вебхука»; латиница — словом целиком. Список дописывается,
    /// когда ревью ловит то, что линтер пропустил
    /// </summary>
    private static readonly string[] StopStems =
    [
        "вебхук", "webhook", "токен", "эндпоинт", "endpoint", "сервер", "контейнер", "докер",
        "очеред", "миграци", "транзакци", "ретра", "антифрод", "эмбеддинг", "админк",
        "себестоимост", "маржа", "маржи", "маржу", "маржой",
    ];

    private static readonly string[] StopWords =
    [
        "qdrant", "mysql", "postgres", "postgresql", "sql", "vllm", "nginx", "jwt", "hmac", "api", "docker", "redis",
        "grafana", "prometheus", "github", "openai", "gpt", "llama", "signalr", "smtp",
    ];

    private static readonly Regex[] StopPhrases =
    [
        new(@"(?<!\p{L})баз\p{L}*\s+данных", RegexOptions.IgnoreCase),
        new(@"(?<!\p{L})подпис\p{L}*\s+запрос", RegexOptions.IgnoreCase),
    ];

    public static List<LintError> Lint(IEnumerable<(string File, string Text)> files, IReadOnlySet<string> routes)
    {
        var errors = new List<LintError>();
        foreach (var (file, text) in files)
        {
            var article = HelpArticleParser.Parse(Path.GetFileNameWithoutExtension(file), text);
            errors.AddRange(article.Problems.Select(x => new LintError(file, null, x)));
            errors.AddRange(LintArticle(file, text, article, routes));
        }

        return errors;
    }

    private static IEnumerable<LintError> LintArticle(
        string file, string text, HelpArticle article, IReadOnlySet<string> routes)
    {
        if (article.Page != null)
        {
            if (!article.Page.StartsWith('/') || AddressRegex().IsMatch(article.Page))
                yield return new LintError(file, null, $"page «{article.Page}» — только путь внутри приложения, от «/»");
            else if (!ClientRoutes.Contains(routes, article.Page))
                yield return new LintError(file, null, $"page «{article.Page}» — такого маршрута у клиента нет");
        }

        if (HelpArticleParser.Tokens(text) > ArticleTokens)
            yield return new LintError(file, null,
                $"статья больше {ArticleTokens} токенов ({HelpArticleParser.Tokens(text)}) — раздели на две темы");

        if (article.Sections.Count == 0)
            yield return new LintError(file, null, "в статье нет ни одного раздела ##");

        foreach (var problem in Words(article.Title)) yield return new LintError(file, null, $"title: {problem}");

        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in article.Sections)
        {
            if (!titles.Add(section.Title))
                yield return new LintError(file, section.Title,
                    "второй раздел с тем же заголовком — по паре «статья + заголовок» поиск узнаёт раздел");

            if (section.Asks == null)
                yield return new LintError(file, section.Title,
                    $"нет строки «{HelpArticleParser.AsksPrefix}» последней строкой раздела");
            else if (section.Asks.Length == 0)
                yield return new LintError(file, section.Title, $"«{HelpArticleParser.AsksPrefix}» без формулировок");

            if (section.Body.Length == 0)
                yield return new LintError(file, section.Title, "раздел без текста");

            var tokens = HelpArticleParser.Tokens(section.ChunkText(article.Title));
            if (tokens > SectionTokens)
                yield return new LintError(file, section.Title,
                    $"раздел больше {SectionTokens} токенов ({tokens}) — это два вопроса, раздели по смыслу");

            var whole = $"{section.Title}\n{section.Body}\n{section.Asks}";
            foreach (var problem in Words(whole)) yield return new LintError(file, section.Title, problem);
        }
    }

    /// <summary>Правила, которые смотрят на сам текст, а не на устройство статьи</summary>
    private static IEnumerable<string> Words(string text)
    {
        if (text.Contains('`') || text.Contains("~~~"))
            yield return "код в справке не бывает — ни inline, ни блоком";

        if (LinkRegex().Match(text) is { Success: true } link)
            yield return $"ссылка «{link.Value}» — в приложение ведёт только поле page";

        if (AddressRegex().Match(text) is { Success: true } address)
            yield return $"адрес «{address.Value}»";

        if (FileRegex().Match(text) is { Success: true } path)
            yield return $"путь или имя файла «{path.Value}»";

        if (DottedRegex().Match(text) is { Success: true } dotted)
            yield return $"идентификатор с точкой «{dotted.Value}»";

        if (SnakeRegex().Match(text) is { Success: true } snake)
            yield return $"идентификатор «{snake.Value}»";

        if (CamelRegex().Match(text) is { Success: true } camel)
            yield return $"идентификатор «{camel.Value}»";

        if (SpecRegex().Match(text) is { Success: true } spec)
            yield return $"ссылка на спецификацию «{spec.Value}» — человеку она ничего не скажет";

        foreach (var stem in StopStems)
            if (Regex.Match(text, $@"(?<!\p{{L}}){stem}", RegexOptions.IgnoreCase) is { Success: true } m)
                yield return $"стоп-слово «{WordAt(text, m.Index)}»";

        foreach (var word in StopWords)
            if (Regex.IsMatch(text, $@"(?<![\p{{L}}\d]){word}(?![\p{{L}}\d])", RegexOptions.IgnoreCase))
                yield return $"стоп-слово «{word}»";

        foreach (var phrase in StopPhrases)
            if (phrase.Match(text) is { Success: true } m)
                yield return $"стоп-слово «{m.Value}»";
    }

    private static string WordAt(string text, int index)
    {
        var end = index;
        while (end < text.Length && char.IsLetter(text[end])) end++;

        return text[index..end];
    }

    [GeneratedRegex(@"\[[^\]]*\]\([^)]*\)|<https?:[^>]*>")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"https?://\S*|www\.\S+|\b\d{1,3}(\.\d{1,3}){3}\b|/api/\S*", RegexOptions.IgnoreCase)]
    private static partial Regex AddressRegex();

    [GeneratedRegex(@"(?<![\p{L}\d])[\w/.-]*\.(cs|md|ts|json|ya?ml|html|scss|csproj|sh)(?![\p{L}\d])",
        RegexOptions.IgnoreCase)]
    private static partial Regex FileRegex();

    /// <summary>
    /// Слово с точкой внутри латиницей — <c>AssistantOptions.Foo</c>, домен. Кириллица
    /// не в счёт: «т.е.» и «и т.д.» — обычный текст
    /// </summary>
    [GeneratedRegex(@"(?<![\p{L}\d])[A-Za-z][A-Za-z\d]*\.[A-Za-z][A-Za-z\d]*")]
    private static partial Regex DottedRegex();

    /// <summary><c>snake_case</c> и <c>UPPER_SNAKE</c> латиницей</summary>
    [GeneratedRegex(@"(?<![\p{L}\d])[A-Za-z\d]+_[A-Za-z\d_]+")]
    private static partial Regex SnakeRegex();

    /// <summary>
    /// <c>CamelCase</c> и <c>camelCase</c>: строчная, за ней заглавная, внутри одного
    /// латинского слова. Аббревиатура целиком заглавными (<c>VK</c>) — не идентификатор:
    /// так называются способы входа на странице входа
    /// </summary>
    [GeneratedRegex(@"(?<![\p{L}\d])[A-Za-z]*[a-z][A-Z][A-Za-z\d]*")]
    private static partial Regex CamelRegex();

    [GeneratedRegex(@"(?<!\p{L})(раздел\p{L}*\s+\d+(\.\d+)*|спец\.|спецификаци\p{L}*|specs/)", RegexOptions.IgnoreCase)]
    private static partial Regex SpecRegex();
}
