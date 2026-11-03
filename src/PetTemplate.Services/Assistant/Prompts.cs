using System.Reflection;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Тексты промптов — ресурсы сборки из <c>Assistant/Prompts/*.md</c> (спец. assistant, 15.2):
/// их правят как текст, а не как склейку строк
/// </summary>
public static class Prompts
{
    public static string Read(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("Assistant.Prompts." + fileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
