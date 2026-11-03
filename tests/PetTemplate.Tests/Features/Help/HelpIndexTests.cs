using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Help;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Options;

namespace PetTemplate.Tests.Features.Help;

/// <summary>
/// Индекс справки в памяти (спец. help, 6): раздел находится по своей строке
/// «Как спрашивают», по заголовку и по слову из тела; без эмбеддера ищет ключевая
/// половина; новая версия подменяет старую, а поиск во время пересборки отвечает прежней
/// </summary>
public sealed class HelpIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pettemplate-help-index-" + Guid.NewGuid().ToString("N"));

    private const string Password = """
                                    ---
                                    title: Пароль
                                    page: /profile/edit
                                    ---

                                    ## Как сменить пароль

                                    В профиле нажмите «Сменить пароль» и введите текущий.

                                    Как спрашивают: поменять пароль; новый пароль

                                    ## Что делать, если забыли пароль

                                    На странице входа нажмите «Забыли пароль?» и выберите путь.

                                    Как спрашивают: восстановить доступ; не помню пароль
                                    """;

    private const string Workspace = """
                                     ---
                                     title: Пространство
                                     ---

                                     ## Как пригласить участника

                                     На странице участников нажмите «Добавить участника» и отправьте ссылку.

                                     Как спрашивают: позвать коллегу; добавить человека
                                     """;

    public HelpIndexTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>Выпуск справки: каталог со статьями и манифестом, как его кладёт публикация</summary>
    private void Release(string commit, params (string Name, string Text)[] articles)
    {
        var directory = Path.Combine(_root, "releases", commit);
        Directory.CreateDirectory(directory);
        foreach (var (name, text) in articles) File.WriteAllText(Path.Combine(directory, name + ".md"), text);
        File.WriteAllText(Path.Combine(directory, HelpManifest.FileName),
            $$"""{"commit":"{{commit}}","publishedAt":"2026-11-01T10:00:00Z","articles":[],"hash":"x"}""");
    }

    /// <summary>Переключить <c>current</c> на выпуск — как публикация, относительной ссылкой</summary>
    private void Switch(string commit)
    {
        var current = Path.Combine(_root, "current");
        if (Directory.Exists(current) || File.Exists(current)) File.Delete(current);
        Directory.CreateSymbolicLink(current, Path.Combine("releases", commit));
    }

    private HelpSearch Search(IEmbeddingGenerator<string, Embedding<float>> embedder, bool embeddings = true,
        TimeProvider? time = null) =>
        new(
            new ServiceCollection().AddSingleton(embedder).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new HelpOptions { Path = Path.Combine(_root, "current") }),
            Options.Create(new SearchOptions
            {
                EmbeddingsEndpoint = embeddings ? "http://embeddings.test/v1" : "",
                EmbeddingsModel = "test-model"
            }),
            NullLogger<HelpSearch>.Instance,
            time: time)
        {
            CacheDirectory = Path.Combine(_root, "cache")
        };

    private async Task<HelpSearch> Published(IEmbeddingGenerator<string, Embedding<float>>? embedder = null)
    {
        Release("a1", ("account", Password), ("workspace", Workspace));
        Switch("a1");
        var help = Search(embedder ?? new KeywordEmbedder(), embedder != null);
        await help.Refresh(CancellationToken.None);

        return help;
    }

    [Theory]
    [InlineData("восстановить доступ", "Что делать, если забыли пароль")] // только в «Как спрашивают»
    [InlineData("как сменить пароль", "Как сменить пароль")] // слова заголовка
    [InlineData("позвать коллегу", "Как пригласить участника")]
    [InlineData("сменю пароль", "Как сменить пароль")] // словоформа слова из тела
    [InlineData("текущего", "Как сменить пароль")]
    public async Task FindsSection(string query, string section)
    {
        // Arrange
        var help = await Published();

        // Act
        var result = await help.Search(query, CancellationToken.None);

        // Assert
        Assert.Equal("a1", result.Commit);
        Assert.Equal(section, result.Hits[0].Section);
    }

    /// <summary>Найденный раздел — целый ответ: тело без «Как спрашивают» и страница статьи</summary>
    [Fact]
    public async Task HitCarriesArticleAndPage()
    {
        // Arrange
        var help = await Published();

        // Act
        var result = await help.Search("восстановить доступ", CancellationToken.None);

        // Assert
        var hit = result.Hits[0];
        Assert.Equal("Пароль", hit.Article);
        Assert.Equal("/profile/edit", hit.Page);
        Assert.StartsWith("На странице входа нажмите", hit.Text);
        Assert.DoesNotContain("Как спрашивают", hit.Text);
    }

    /// <summary>Мимо справки — пустая выдача, а не три случайных раздела</summary>
    [Fact]
    public async Task UnrelatedQueryFindsNothing()
    {
        // Arrange
        var help = await Published(new KeywordEmbedder());

        // Act
        var result = await help.Search("какая погода в Сочи", CancellationToken.None);

        // Assert
        Assert.True(result.Available);
        Assert.Empty(result.Hits);
    }

    /// <summary>Векторная половина находит то, где слов вопроса в тексте нет</summary>
    [Fact]
    public async Task VectorFindsWithoutSharedWords()
    {
        // Arrange
        var help = await Published(new KeywordEmbedder());

        // Act
        var result = await help.Search("не пускает в аккаунт", CancellationToken.None);

        // Assert
        Assert.NotEmpty(result.Hits);
        Assert.All(result.Hits, x => Assert.Equal("Пароль", x.Article));
    }

    /// <summary>
    /// Префиксы e5 (спец. search, 3): разделы считаются как документ, вопрос — как запрос.
    /// Без знака эмбеддер сравнивал бы вопрос с разделами в разных шкалах
    /// </summary>
    [Fact]
    public async Task EmbedderGetsPassagesAndQuery()
    {
        // Arrange
        var embedder = new KeywordEmbedder();
        var help = await Published(embedder);

        // Act
        await help.Search("не пускает в аккаунт", CancellationToken.None);

        // Assert
        Assert.All(embedder.Calls.Where(x => x.Text.Contains('›')), x => Assert.Equal(EmbeddingPurpose.Passage, x.Purpose));
        Assert.Contains(("не пускает в аккаунт", EmbeddingPurpose.Query), embedder.Calls);
    }

    /// <summary>Эмбеддер лежит — ищет ключевая половина, поиск не падает</summary>
    [Fact]
    public async Task BrokenEmbedderFallsBackToKeywords()
    {
        // Arrange
        var help = await Published(new FailingEmbedder());

        // Act
        var result = await help.Search("восстановить доступ", CancellationToken.None);

        // Assert
        Assert.Equal("Что делать, если забыли пароль", result.Hits[0].Section);
    }

    /// <summary>Нет каталога или манифеста — «справка недоступна», а не исключение</summary>
    [Fact]
    public async Task NoManifestMeansUnavailable()
    {
        // Arrange
        var help = Search(new KeywordEmbedder());
        await help.Refresh(CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(_root, "current"));
        await help.Refresh(CancellationToken.None);

        // Act
        var result = await help.Search("пароль", CancellationToken.None);

        // Assert
        Assert.False(result.Available);
        Assert.Empty(result.Hits);
    }

    /// <summary>
    /// Новая версия подменяет старую одной ссылкой, а пока строится — отвечает прежняя
    /// </summary>
    [Fact]
    public async Task SearchDuringRebuildAnswersPreviousVersion()
    {
        // Arrange
        var embedder = new GatedEmbedder();
        var help = await Published(embedder);
        Release("b2", ("account", Password.Replace("и введите текущий.", "и введите текущий. Годится и Telegram.")),
            ("workspace", Workspace));
        Switch("b2");
        embedder.Close();

        // Act
        var rebuild = help.Refresh(CancellationToken.None);
        var during = await help.Search("Telegram", CancellationToken.None);
        embedder.Open();
        await rebuild;
        var after = await help.Search("Telegram", CancellationToken.None);

        // Assert
        Assert.Equal("a1", during.Commit);
        Assert.Empty(during.Hits);
        Assert.Equal("b2", after.Commit);
        Assert.Equal("Как сменить пароль", after.Hits[0].Section);
    }

    /// <summary>Векторы из кэша: новая версия пересчитывает только изменившийся раздел</summary>
    [Fact]
    public async Task CacheRecomputesOnlyChangedSections()
    {
        // Arrange
        var help = await Published(new KeywordEmbedder());
        var first = help.Current!.RecomputedVectors;
        Release("b2", ("account", Password.Replace("и введите текущий.", "и введите текущий. Годится и Telegram.")),
            ("workspace", Workspace));
        Switch("b2");

        // Act
        await help.Refresh(CancellationToken.None);

        // Assert
        Assert.Equal(3, first);
        Assert.Equal(1, help.Current!.RecomputedVectors);
    }

    /// <summary>Тот же коммит — индекс не пересобирается</summary>
    [Fact]
    public async Task SameCommitIsNotRebuilt()
    {
        // Arrange
        var help = await Published();
        var index = help.Current;

        // Act
        await help.Refresh(CancellationToken.None);

        // Assert
        Assert.Same(index, help.Current);
    }

    /// <summary>
    /// Эмбеддер лежал при сборке — тот же выпуск пересобирается, когда пауза вышла,
    /// и разделы догоняют векторы без нового выпуска и рестарта (6.4)
    /// </summary>
    [Fact]
    public async Task SameCommitCatchesUpMissingVectors()
    {
        // Arrange
        var embedder = new FlakyEmbedder { IsDown = true };
        var clock = new ManualClock();
        Release("a1", ("account", Password), ("workspace", Workspace));
        Switch("a1");
        var help = Search(embedder, time: clock);
        await help.Refresh(CancellationToken.None);
        var missing = help.Current!.MissingVectors;
        embedder.IsDown = false;
        clock.Advance(new HelpOptions().VectorRetryDelay);

        // Act
        await help.Refresh(CancellationToken.None);

        // Assert
        Assert.Equal(3, missing);
        Assert.Equal("a1", help.Current?.Commit);
        Assert.Equal(0, help.Current?.MissingVectors);
    }

    /// <summary>
    /// Пока эмбеддер лежит, повтор ждёт паузу, и каждая неудача её удваивает: опрос раз
    /// в минуту не упирается каждый раз в таймауты (6.4)
    /// </summary>
    [Fact]
    public async Task MissingVectorsRetryWithGrowingDelay()
    {
        // Arrange
        var embedder = new FlakyEmbedder { IsDown = true };
        var clock = new ManualClock();
        var delay = new HelpOptions().VectorRetryDelay;
        Release("a1", ("account", Password), ("workspace", Workspace));
        Switch("a1");
        var help = Search(embedder, time: clock);
        await help.Refresh(CancellationToken.None);
        var attempts = new List<int> { embedder.Attempts };

        // Act
        await help.Refresh(CancellationToken.None);
        attempts.Add(embedder.Attempts);
        clock.Advance(delay);
        await help.Refresh(CancellationToken.None);
        attempts.Add(embedder.Attempts);
        clock.Advance(delay);
        await help.Refresh(CancellationToken.None);
        attempts.Add(embedder.Attempts);
        clock.Advance(delay);
        await help.Refresh(CancellationToken.None);
        attempts.Add(embedder.Attempts);

        // Assert: сразу — нет, через паузу — да, через ту же паузу — нет (она удвоилась), через две — да
        Assert.Equal([1, 1, 2, 2, 3], attempts);
    }

    /// <summary>Эмбеддер не настроен — векторов не ждём и тот же выпуск не пересобираем</summary>
    [Fact]
    public async Task NoEmbedderMeansNoVectorRetry()
    {
        // Arrange
        var clock = new ManualClock();
        Release("a1", ("account", Password), ("workspace", Workspace));
        Switch("a1");
        var help = Search(new KeywordEmbedder(), embeddings: false, time: clock);
        await help.Refresh(CancellationToken.None);
        var index = help.Current;
        clock.Advance(TimeSpan.FromDays(1));

        // Act
        await help.Refresh(CancellationToken.None);

        // Assert
        Assert.Same(index, help.Current);
    }

    /// <summary>
    /// Статья для сайта (6.5): разделы markdown-текстом, без фронтматтера и строк
    /// «Как спрашивают» — они только для поиска
    /// </summary>
    [Fact]
    public async Task PagesCarryMarkdownWithoutAsks()
    {
        // Arrange
        var help = await Published();

        // Act
        var page = help.Current!.Pages.Single(x => x.Slug == "account");

        // Assert
        Assert.Equal("Пароль", page.Title);
        Assert.StartsWith("## Как сменить пароль\n\nВ профиле нажмите", page.Markdown);
        Assert.Contains("## Что делать, если забыли пароль", page.Markdown);
        Assert.DoesNotContain("Как спрашивают", page.Markdown);
        Assert.DoesNotContain("page:", page.Markdown);
    }

    /// <summary>Ключ раздела — пара «статья + заголовок»: правка тела его не меняет</summary>
    [Fact]
    public void SectionIdIgnoresBody()
    {
        // Assert
        Assert.Equal(HelpIndex.SectionId("account", "Пароль"), HelpIndex.SectionId("account", "Пароль"));
        Assert.NotEqual(HelpIndex.SectionId("account", "Пароль"), HelpIndex.SectionId("workspace", "Пароль"));
    }

    /// <summary>
    /// Эмбеддер-заглушка: две оси — «про вход» и «про пространство». Достаточно, чтобы
    /// векторная половина находила раздел без общих с вопросом слов
    /// </summary>
    private class KeywordEmbedder : IEmbeddingGenerator<string, Embedding<float>>
    {
        private static readonly string[] Account = ["аккаунт", "парол", "вход", "доступ"];

        private static readonly string[] Space = ["пространств", "участник", "коллег"];

        /// <summary>Что считали и зачем (<see cref="EmbeddingPurpose"/>)</summary>
        public ConcurrentQueue<(string Text, string? Purpose)> Calls { get; } = new();

        public virtual Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(x =>
            {
                Calls.Enqueue((x, EmbeddingPurpose.Of(options)));
                var text = x.ToLowerInvariant();

                return new Embedding<float>(new[]
                {
                    Account.Any(text.Contains) ? 1f : 0f,
                    Space.Any(text.Contains) ? 1f : 0f,
                    Account.Any(text.Contains) || Space.Any(text.Contains) ? 0f : 1f
                });
            })));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FailingEmbedder : KeywordEmbedder
    {
        public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("embedder is down");
    }

    /// <summary>Эмбеддер, который лежит, пока <see cref="IsDown"/>; считает попытки сборки</summary>
    private sealed class FlakyEmbedder : KeywordEmbedder
    {
        public bool IsDown { get; set; }

        public int Attempts { get; private set; }

        public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Attempts++;

            return IsDown
                ? throw new HttpRequestException("embedder is down")
                : base.GenerateAsync(values, options, cancellationToken);
        }
    }

    /// <summary>Часы, которые двигает тест</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Эмбеддер, который можно задержать — чтобы поймать поиск посреди пересборки</summary>
    private sealed class GatedEmbedder : KeywordEmbedder
    {
        private TaskCompletionSource _gate = CompletedGate();

        public void Close() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _gate.TrySetResult();

        public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = values.ToList();
            // Вектор вопроса — сразу: задерживаем только сборку индекса
            if (list.Count > 1 || list.Any(x => x.Contains('›'))) await _gate.Task.WaitAsync(cancellationToken);

            return await base.GenerateAsync(list, options, cancellationToken);
        }

        private static TaskCompletionSource CompletedGate()
        {
            var gate = new TaskCompletionSource();
            gate.SetResult();

            return gate;
        }
    }
}
