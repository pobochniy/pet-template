using PetTemplate.HelpCheck;

namespace PetTemplate.Tests.Features.Help;

/// <summary>
/// Ссылки на спецификации из кода и документов (<c>HelpCheck refs</c>, спец. infrastructure, 5):
/// раздел перенумеровали или убрали — ссылка «спец. X, N.M» краснеет в ПР, а не врёт годами
/// </summary>
public class SpecRefsTests
{
    private static readonly Dictionary<string, HashSet<string>> Specs = new()
    {
        ["admin"] = SpecRefs.Sections("""
                                      # Админка

                                      ## 3. Вход

                                      ### 3.5 Отказы

                                      ```
                                      ## 9. Не заголовок — внутри блока кода
                                      ```

                                      ## 7. Замок
                                      """)
    };

    /// <summary>
    /// Ссылки в данных теста записаны через SPEC и SPECS — иначе <c>refs</c> по репозиторию
    /// нашёл бы нарочно мёртвые ссылки в этом файле
    /// </summary>
    private static List<RefError> Check(string line) =>
        [.. SpecRefs.Check("Program.cs", line.Replace("SPECS", "specs").Replace("SPEC", "спец."), Specs)];

    [Theory]
    [InlineData("// Отказ — пустой 404 (SPEC admin, 3.5)")]
    [InlineData("# замок (SPEC admin, 3.5 и 7)")]
    [InlineData("см. `SPECS/admin.md`, раздел 7")]
    [InlineData("см. `SPECS/admin.md` (разделы 3 и 7)")]
    [InlineData("(SPEC admin; без номера — только файл)")]
    public void LiveReferencePasses(string line)
    {
        // Act
        var errors = Check(line);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("// (SPEC admin, 3.6)", "3.6")]
    [InlineData("# (SPEC admin, 3.5 и 8)", "8")]
    [InlineData("`SPECS/admin.md`, 9", "9")]
    public void DeadSectionFails(string line, string section)
    {
        // Act
        var error = Assert.Single(Check(line));

        // Assert
        Assert.Contains($"нет раздела {section}", error.Reason);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void MissingSpecFails()
    {
        // Act
        var error = Assert.Single(Check("// (SPEC billing, 2)"));

        // Assert
        Assert.Contains("billing.md", error.Reason);
    }

    /// <summary>
    /// Хэш раздела не зависит от имени проекта и домена: <c>dotnet new</c> их заменяет,
    /// и карта, посчитанная в шаблоне, должна сходиться в проекте из него
    /// </summary>
    [Fact]
    public void SectionHashIgnoresTemplateSymbols()
    {
        // Act
        var template = SpecOutline.Hash("Сайт PetTemplate, база pettemplatedb, адрес example.com");
        var placeholders = SpecOutline.Hash("Сайт {Name}, база {name}db, адрес {domain}");

        // Assert
        Assert.Equal(placeholders, template);
    }
}
