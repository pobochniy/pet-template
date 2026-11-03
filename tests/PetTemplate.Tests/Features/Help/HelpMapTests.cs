using PetTemplate.HelpCheck;

namespace PetTemplate.Tests.Features.Help;

/// <summary>
/// Сверка карты справки со спецификациями (спец. help, 5.2): новый раздел,
/// изменённый, пропавший — падает; перенумерованный (номер другой, текст тот же) —
/// нет. Так «смержили фичу — справка о ней подумала» держится на проверке, а не на памяти
/// </summary>
public sealed class HelpMapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pettemplate-help-map-" + Guid.NewGuid().ToString("N"));

    private const string Spec = """
                                # Спецификация: аккаунт

                                ## 1. Зачем

                                Один человек — один аккаунт.

                                ## 2. Пароль

                                ### 2.1 Сменить

                                Текущий и новый дважды.

                                ### 2.2 Хеш

                                Техника.
                                """;

    public HelpMapTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "specs", "strategy"));
        Directory.CreateDirectory(Path.Combine(_root, "help"));
        File.WriteAllText(Path.Combine(_root, "specs", "accounts.md"), Spec);
        File.WriteAllText(Path.Combine(_root, "specs", "admin.md"), "# Админка\n\n## 1. Всё\n\nТекст.\n");
        // Стратегия — не спецификация и в карту не входит
        File.WriteAllText(Path.Combine(_root, "specs", "strategy", "01_Next.md"), "## Дальше\n\nПланы.\n");
        File.WriteAllText(Path.Combine(_root, "help", "account.md"), "---\ntitle: Аккаунт\n---\n");
    }

    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>Разнести карту: «Зачем» и «Сменить» — в статью, «Хеш» — skip, админка целиком skip</summary>
    private void Arrange()
    {
        Assert.Empty(HelpMap.Init(_root, "accounts.md"));
        var map = File.ReadAllText(HelpMap.MapPath(_root))
            .Replace("\"Зачем\": todo", "\"Зачем\": { article: account, hash: \"\" }")
            .Replace("\"Сменить\": todo", "\"Сменить\": { article: account, hash: \"\" }")
            .Replace("\"Хеш\": todo", "\"Хеш\": skip")
            + "\nadmin.md: skip\n";
        File.WriteAllText(HelpMap.MapPath(_root), map);
        Assert.Empty(HelpMap.Rehash(_root, "accounts.md", null));
    }

    private void EditSpec(Func<string, string> edit)
    {
        var path = Path.Combine(_root, "specs", "accounts.md");
        File.WriteAllText(path, edit(File.ReadAllText(path)));
    }

    [Fact]
    public void ArrangedMapPasses()
    {
        // Arrange
        Arrange();

        // Act
        var errors = HelpMap.Check(_root);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Заготовка <c>--init</c> карту не проходит: разделы ещё никто не разнёс.
    /// <c>##</c> с <c>###</c> внутри и без вступления — не единица
    /// </summary>
    [Fact]
    public void InitWritesTodoForLeaves()
    {
        // Act
        HelpMap.Init(_root, "accounts.md");
        var errors = HelpMap.Check(_root);

        // Assert
        var map = File.ReadAllText(HelpMap.MapPath(_root));
        Assert.Contains("\"Зачем\": todo", map);
        Assert.Contains("\"Сменить\": todo", map);
        Assert.DoesNotContain("\"Пароль\"", map);
        Assert.Contains(errors, x => x.Contains("Зачем") && x.Contains("не разнесён"));
        Assert.Contains(errors, x => x.Contains("admin.md") && x.Contains("нет в карте"));
        Assert.DoesNotContain(errors, x => x.Contains("strategy") || x.Contains("01_Next"));
    }

    [Fact]
    public void NewSectionFails()
    {
        // Arrange
        Arrange();
        EditSpec(x => x + "\n\n### 2.3 Сбросить\n\nПо ссылке из письма.\n");

        // Act
        var errors = HelpMap.Check(_root);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("accounts.md › Сбросить: раздела нет в карте", error);
    }

    /// <summary>Раздел со статьёй поменялся, а статью не сверили</summary>
    [Fact]
    public void ChangedSectionFails()
    {
        // Arrange
        Arrange();
        EditSpec(x => x.Replace("Текущий и новый дважды.", "Текущий и новый дважды, без логина."));

        // Act
        var errors = HelpMap.Check(_root);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("Сменить: раздел поменялся", error);
    }

    /// <summary>Правка раздела skip справку не будит — хэша у него нет</summary>
    [Fact]
    public void ChangedSkipSectionPasses()
    {
        // Arrange
        Arrange();
        EditSpec(x => x.Replace("Техника.", "Техника, и ещё техника."));

        // Act
        var errors = HelpMap.Check(_root);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void RemovedSectionFails()
    {
        // Arrange
        Arrange();
        EditSpec(x => x.Replace("### 2.1 Сменить", "### 2.1 Сменить пароль"));

        // Act
        var errors = HelpMap.Check(_root);

        // Assert
        Assert.Contains(errors, x => x.Contains("› Сменить: в карте есть, а в спецификации такого раздела больше нет"));
        Assert.Contains(errors, x => x.Contains("› Сменить пароль: раздела нет в карте"));
    }

    /// <summary>Номер сменился, текст тот же — не падает: ключ без номера, хэш без заголовка</summary>
    [Fact]
    public void RenumberedSectionPasses()
    {
        // Arrange
        Arrange();
        EditSpec(x => x.Replace("## 1. Зачем", "## 3. Зачем").Replace("### 2.1 Сменить", "### 4.7 Сменить"));

        // Act
        var errors = HelpMap.Check(_root);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void MissingArticleFails()
    {
        // Arrange
        Arrange();
        File.Delete(Path.Combine(_root, "help", "account.md"));

        // Act
        var errors = HelpMap.Check(_root);

        // Assert
        Assert.Contains(errors, x => x.Contains("статьи account нет"));
    }

    /// <summary>Одинаковые заголовки в одном файле различаются родителем</summary>
    [Fact]
    public void DuplicateTitlesGetParent()
    {
        // Act
        var units = SpecOutline.Units("## 1. Пользователь\n\n### Свойства\n\nА.\n\n## 2. Пространство\n\n### Свойства\n\nБ.\n");

        // Assert
        Assert.Equal(["Пользователь › Свойства", "Пространство › Свойства"], units.Select(x => x.Key));
    }
}
