using System.Text.RegularExpressions;

namespace PetTemplate.HelpCheck;

/// <summary>
/// Маршруты клиента, куда справка может отправить человека полем <c>page</c>
/// (спец. help, 4.2). Читаются из исходников <c>*.routes.ts</c>, а не из списка
/// в утилите: переименовали страницу — статья со старым адресом падает в ПР.
///
/// Разбор нарочно простой — регулярками, без TypeScript: корневой
/// <c>app.routes.ts</c> даёт префиксы, а <c>loadChildren</c> с импортом файла
/// маршрутов приклеивает к префиксу пути из этого файла (у PetTemplate <c>import</c> обёрнут
/// в <c>withScope</c> — словарь раздела едет вместе с ним). Глубже одного уровня
/// вложенности маршрутов у клиента нет. Админка — отдельный сайт со своим
/// клиентом, и её маршруты сюда не попадают по построению
/// </summary>
public static partial class ClientRoutes
{
    public const string AppDirectory = "src/PetTemplate.Web/web/src/app";

    public static HashSet<string> Load(string repositoryRoot)
    {
        var app = Path.Combine(repositoryRoot, AppDirectory);
        var routes = new HashSet<string>(StringComparer.Ordinal);
        var root = File.ReadAllText(Path.Combine(app, "app.routes.ts"));

        foreach (var entry in Entries(root))
        {
            var prefix = Join("", entry.Path);
            routes.Add(prefix);
            if (entry.ChildrenFile == null) continue;

            var file = Path.GetFullPath(Path.Combine(app, entry.ChildrenFile + ".ts"));
            foreach (var child in Entries(File.ReadAllText(file)))
                routes.Add(Join(prefix, child.Path));
        }

        return routes;
    }

    /// <summary>
    /// Есть ли <paramref name="page"/> среди маршрутов: параметр маршрута
    /// (<c>:invoiceId</c>) совпадает с любым сегментом
    /// </summary>
    public static bool Contains(IReadOnlySet<string> routes, string page)
    {
        if (routes.Contains(page)) return true;

        var segments = page.Split('/');

        return routes.Any(route =>
        {
            var pattern = route.Split('/');

            return pattern.Length == segments.Length
                   && pattern.Zip(segments).All(x => x.First.StartsWith(':') || x.First == x.Second);
        });
    }

    private static string Join(string prefix, string path) =>
        "/" + string.Join('/', new[] { prefix.Trim('/'), path.Trim('/') }.Where(x => x.Length > 0));

    private static IEnumerable<(string Path, string? ChildrenFile)> Entries(string source)
    {
        var paths = PathRegex().Matches(source);
        for (var i = 0; i < paths.Count; i++)
        {
            // loadChildren относится к тому маршруту, после чьего path он стоит
            // и до следующего path
            var end = i + 1 < paths.Count ? paths[i + 1].Index : source.Length;
            var tail = source[paths[i].Index..end];
            var children = ChildrenRegex().Match(tail);

            yield return (paths[i].Groups[1].Value, children.Success ? children.Groups[1].Value : null);
        }
    }

    [GeneratedRegex(@"\bpath:\s*'([^']*)'")]
    private static partial Regex PathRegex();

    /// <summary>
    /// <c>import</c> файла маршрутов — и голый, и обёрнутый: у PetTemplate словарь раздела
    /// подгружается вместе с ним, <c>withScope('profile', import('./profile/profile.routes'), 'auth')</c>
    /// </summary>
    [GeneratedRegex(@"loadChildren:\s*\(\)\s*=>[^;\n]*?import\('([^']+)'\)")]
    private static partial Regex ChildrenRegex();
}
