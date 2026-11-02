namespace PetTemplate.HelpCheck;

/// <summary>Где в репозитории лежат справка и спецификации</summary>
public static class Repository
{
    public const string HelpDirectory = "help";

    public const string SpecsDirectory = "specs";

    /// <summary>Файл карты: раздел спецификации → статья или «не для справки» (5.1)</summary>
    public const string MapFile = "_map.yml";

    /// <summary>
    /// Корень — явно заданный или ближайший вверх от текущего каталога, где лежит
    /// <c>PetTemplate.slnx</c>: утилиту зовут и из корня, и из CI, и из каталога проекта
    /// </summary>
    public static string Root(string? explicitRoot)
    {
        if (explicitRoot != null) return Path.GetFullPath(explicitRoot);

        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PetTemplate.slnx")))
                return dir.FullName;

        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Статьи справки: <c>*.md</c> верхнего уровня, служебные файлы с подчёркиванием
    /// (<c>_map.yml</c>) — не статьи
    /// </summary>
    public static IEnumerable<(string File, string Text)> Articles(string helpDirectory) =>
        Directory.Exists(helpDirectory)
            ? Directory.GetFiles(helpDirectory, "*.md")
                .Where(x => !Path.GetFileName(x).StartsWith('_'))
                .Order(StringComparer.Ordinal)
                .Select(x => (Path.GetFileName(x), File.ReadAllText(x)))
            : [];
}
