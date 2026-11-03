using System.Text.Json;

namespace PetTemplate.Services.Help;

/// <summary>
/// Манифест выпуска справки (спец. help, 6.4): его пишет публикация, по нему API
/// замечает новую версию. Коммит — тот же, что у выкаченного кода
/// </summary>
public record HelpManifest(string Commit, DateTimeOffset PublishedAt, IReadOnlyList<string> Articles, string Hash)
{
    public const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Манифест из каталога выпуска; нет файла или он не читается — <c>null</c></summary>
    public static HelpManifest? Read(string directory)
    {
        var path = System.IO.Path.Combine(directory, FileName);
        if (!File.Exists(path)) return null;

        var manifest = JsonSerializer.Deserialize<HelpManifest>(File.ReadAllText(path), Json);

        return string.IsNullOrWhiteSpace(manifest?.Commit) ? null : manifest;
    }
}
