using PetTemplate.Help;
using PetTemplate.HelpCheck;

namespace PetTemplate.Tests.Features.Help;

/// <summary>
/// Линтер справки (спец. help, 4.2): на каждое правило — статья, которая его
/// нарушает, и строка ошибки «файл, раздел, что не так». Справка с ошибкой не уезжает
/// ни из ПР, ни при выкатке — ассистент перескажет её кому угодно
/// </summary>
public class HelpLintTests
{
    private static readonly HashSet<string> Routes = ["/", "/workspace/settings", "/invite/:token", "/profile/edit"];

    private const string Asks = "Как спрашивают: что такое пространство; зачем пространство";

    private static string Article(string body, string frontMatter = "title: Пространство\npage: /workspace/settings") =>
        $"---\n{frontMatter}\n---\n\n## Что такое пространство\n\n{body}\n\n{Asks}\n";

    private static List<LintError> Lint(string text) => HelpLinter.Lint([("workspace.md", text)], Routes);

    /// <summary>Чистая статья проходит — иначе линтер бесполезен</summary>
    [Fact]
    public void CleanArticlePasses()
    {
        // Act
        var errors = Lint(Article(
            "Пространство — общий дом для данных и людей. Войти можно и через Telegram или VK."));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Разбор: фронтматтер, разделы, «Как спрашивают» отдельно от тела</summary>
    [Fact]
    public void ParserSplitsSectionsAndAsks()
    {
        // Act
        var article = HelpArticleParser.Parse("workspace",
            "---\ntitle: Пространство\npage: /workspace/settings\n---\n\n## Первый\n\nТекст.\n\n### Подробно\n\nЕщё.\n\n" +
            "Как спрашивают: раз; два\n\n## Второй\n\nДругое.\n\nКак спрашивают: три\n");

        // Assert
        Assert.Equal("Пространство", article.Title);
        Assert.Equal("/workspace/settings", article.Page);
        Assert.Collection(article.Sections,
            x =>
            {
                Assert.Equal("Первый", x.Title);
                Assert.Contains("### Подробно", x.Body);
                Assert.Equal("раз; два", x.Asks);
            },
            x => Assert.Equal("Второй", x.Title));
        Assert.StartsWith("Пространство › Первый\n\n", article.Sections[0].ChunkText(article.Title));
    }

    [Theory]
    [InlineData("Нажмите `Выйти`.", "код")]
    [InlineData("Поле WorkspaceOptions хранит название.", "идентификатор")]
    [InlineData("Поле primaryWorkspaceId хранит основное.", "идентификатор")]
    [InlineData("Смотрите user_in_permissions в списке.", "идентификатор")]
    [InlineData("Режим PERMISSION_MANAGEMENT включён.", "идентификатор")]
    [InlineData("Настройка Workspace.Foo задаёт срок.", "с точкой")]
    [InlineData("Описано в members.component.ts рядом.", "файл")]
    [InlineData("Откройте https://pettemplate.example/workspace и нажмите.", "адрес")]
    [InlineData("Адрес 10.0.0.1 недоступен.", "адрес")]
    [InlineData("Запрос уходит на /api/workspace сразу.", "адрес")]
    [InlineData("Смотрите [страницу участников](/workspace/members).", "ссылка")]
    [InlineData("Подробнее — раздел 5.3.", "спецификаци")]
    [InlineData("Так решено в спец. авторизации.", "спецификаци")]
    [InlineData("Ответ приходит вебхуком от площадки.", "вебхук")]
    [InlineData("Письмо ждёт в очереди.", "очеред")]
    [InlineData("Права хранятся в базе данных.", "базе данных")]
    [InlineData("Работает на Qdrant и PostgreSQL.", "qdrant")]
    [InlineData("Сессия — это токен на сутки.", "токен")]
    [InlineData("Вход проверяется подписью запроса площадки.", "подписью запрос")]
    public void ForbiddenTextFails(string body, string expected)
    {
        // Act
        var errors = Lint(Article(body));

        // Assert
        Assert.NotEmpty(errors);
        Assert.All(errors, x => Assert.Equal(("workspace.md", "Что такое пространство"), (x.File, x.Section)));
        Assert.Contains(errors, x => x.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Аббревиатура со страницы входа — не идентификатор, «т.е.» — не путь</summary>
    [Theory]
    [InlineData("Войти можно через VK или Telegram.")]
    [InlineData("Права общие, т.е. принадлежат пространству.")]
    public void VisibleNamesPass(string body)
    {
        // Act
        var errors = Lint(Article(body));

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("title: Пространство\npage: /workspaces", "такого маршрута у клиента нет")]
    [InlineData("title: Пространство\npage: https://pettemplate.example/workspace", "только путь")]
    [InlineData("page: /workspace/settings", "нет title")]
    public void BadFrontMatterFails(string frontMatter, string expected)
    {
        // Act
        var errors = Lint(Article("Пространство — общий дом.", frontMatter));

        // Assert
        Assert.Contains(errors, x => x.Section == null && x.Message.Contains(expected));
    }

    /// <summary>Параметр маршрута совпадает с любым сегментом</summary>
    [Fact]
    public void RouteParameterMatches()
    {
        // Act
        var errors = Lint(Article("Пространство — общий дом.", "title: Пространство\npage: /invite/abc"));

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void SectionWithoutAsksFails()
    {
        // Act
        var errors = Lint("---\ntitle: Пространство\n---\n\n## Что такое пространство\n\nОбщий дом.\n");

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal("Что такое пространство", error.Section);
        Assert.Contains("Как спрашивают", error.Message);
    }

    [Fact]
    public void LongSectionFails()
    {
        // Act
        var errors = Lint(Article(string.Join(' ', Enumerable.Repeat("Пространство — общий дом для данных.", 50))));

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains($"больше {HelpLinter.SectionTokens} токенов", error.Message);
    }

    [Fact]
    public void LongArticleFails()
    {
        // Arrange
        var sections = Enumerable.Range(1, 20)
            .Select(i => $"## Вопрос {i}\n\n{string.Join(' ', Enumerable.Repeat("Общий дом.", 70))}\n\n{Asks}\n");

        // Act
        var errors = Lint("---\ntitle: Пространство\n---\n\n" + string.Join('\n', sections));

        // Assert
        Assert.Contains(errors, x => x.Section == null && x.Message.Contains($"больше {HelpLinter.ArticleTokens}"));
    }

    /// <summary>Пара «статья + заголовок» — постоянный ключ раздела в поиске</summary>
    [Fact]
    public void DuplicateSectionTitleFails()
    {
        // Act
        var errors = Lint($"---\ntitle: Пространство\n---\n\n## Выход\n\nКнопкой.\n\n{Asks}\n\n## Выход\n\nИз списка.\n\n{Asks}\n");

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("тем же заголовком", error.Message);
    }

    [Fact]
    public void TextOutsideSectionsFails()
    {
        // Act
        var errors = Lint("---\ntitle: Пространство\n---\n\nВступление.\n\n## Вопрос\n\nОтвет.\n\n" + Asks + "\n");

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("до первого раздела", error.Message);
    }

    /// <summary>
    /// Маршруты читаются из настоящих <c>*.routes.ts</c> клиента PetTemplate — и те, у которых
    /// <c>import</c> обёрнут в <c>withScope</c>: иначе статья с <c>page: /workspace/members</c>
    /// падала бы, хотя такая страница есть
    /// </summary>
    [Fact]
    public void ClientRoutesAreReadFromTheRepository()
    {
        // Act
        var routes = ClientRoutes.Load(Repository.Root(null));

        // Assert
        Assert.Contains("/workspace/members", routes);
        Assert.Contains("/profile/edit", routes);
        Assert.Contains("/auth/recover", routes);
        Assert.True(ClientRoutes.Contains(routes, "/invite/42"));
    }
}
