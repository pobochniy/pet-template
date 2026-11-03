namespace PetTemplate.HelpCheck;

/// <summary>
/// Проверка справки (спец. help, 4.2 и 5.2) и ссылок на спецификации (спец. infrastructure, 5). Без сети, без модели, без базы:
/// детерминированная проверка, которая одинаково отвечает в ПР и при публикации.
///
/// <code>
///   lint [--root &lt;репозиторий&gt;]        статьи help/ — правила 4.2
///   map  [--root &lt;репозиторий&gt;]        сверка карты со specs/*.md — 5.2
///   map --rehash &lt;файл&gt; [&lt;раздел&gt;]     пересчитать хэши разделов со статьёй
///   map --init &lt;файл&gt;                  дописать в карту новые разделы заготовкой
///   refs [--root &lt;репозиторий&gt;]       ссылки «спец. X, N.M» в коде и документах ведут в живой раздел
/// </code>
///
/// Код выхода: 0 — зелёно, 1 — есть ошибки, 2 — не так вызвали.
///
/// Класс, а не top-level: тесты ссылаются и на эту сборку, и на API, а два
/// глобальных <c>Program</c> в одной компиляции не уживаются
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var root = Repository.Root(Option(args, "--root"));
        var help = Path.Combine(root, Repository.HelpDirectory);

        switch (args.FirstOrDefault())
        {
            case "lint":
                return Report(HelpLinter.Lint(Repository.Articles(help), ClientRoutes.Load(root)), "lint");

            case "map" when Option(args, "--rehash") is { } file:
                return Report(HelpMap.Rehash(root, file, Positional(args, "--rehash")), "map --rehash");

            case "map" when Option(args, "--init") is { } file:
                return Report(HelpMap.Init(root, file), "map --init");

            case "map":
                return Report(HelpMap.Check(root), "map");

            case "refs":
                return Report(SpecRefs.Check(root), "refs");

            default:
                Console.Error.WriteLine(
                    "Использование: HelpCheck lint | map [--rehash <файл> [<раздел>] | --init <файл>] | refs [--root <путь>]");

                return 2;
        }
    }

    private static int Report<T>(IReadOnlyCollection<T> errors, string command)
    {
        foreach (var error in errors) Console.WriteLine(error);
        Console.WriteLine(errors.Count == 0 ? $"{command}: зелёно" : $"{command}: ошибок — {errors.Count}");

        return errors.Count == 0 ? 0 : 1;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>Второй аргумент после опции — имя раздела у <c>--rehash</c></summary>
    private static string? Positional(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 2 < args.Length && !args[index + 2].StartsWith("--") ? args[index + 2] : null;
    }
}
