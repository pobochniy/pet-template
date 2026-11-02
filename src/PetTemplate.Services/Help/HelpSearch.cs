using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Help;

/// <summary>Ответ поиска по справке: нашлось, не нашлось или справки нет вовсе</summary>
/// <param name="Commit">Коммит справки, на которой искали; <c>null</c> — справка недоступна</param>
public record HelpSearchResult(string? Commit, IReadOnlyList<HelpHit> Hits)
{
    public bool Available => Commit != null;
}

/// <summary>
/// Справка в памяти API (спец. help, 6.1 и 6.4): держит текущий индекс, ищет по нему
/// и подменяет его новой версией. Синглтон — индекс один на процесс, контейнер API один.
///
/// Эмбеддер — типизированный клиент с коротким временем жизни, поэтому берётся из
/// области на каждую сборку и каждый поиск, а не держится здесь.
///
/// Метрики (доля пустых поисков, версия справки, спец. help, 10) живут в метриках
/// ассистента, как в Lad: в справку ходит <c>search_help</c>
/// </summary>
public class HelpSearch(
    IServiceScopeFactory scopeFactory,
    IOptions<HelpOptions> options,
    IOptions<SearchOptions> searchOptions,
    ILogger<HelpSearch> logger,
    AssistantMetrics? metrics = null)
{
    /// <summary>
    /// Сколько ждём вектор запроса: эмбеддер считает короткую строку за десятки
    /// миллисекунд, дольше — он лежит, и ищет ключевая половина
    /// </summary>
    private static readonly TimeSpan QueryVectorTimeout = TimeSpan.FromSeconds(5);

    private HelpIndex? _current;

    /// <summary>Последнее состояние «справки нет», о котором уже написали в лог</summary>
    private string? _loggedProblem;

    /// <summary>Каталог кэша векторов; тесты подставляют свой</summary>
    public string? CacheDirectory { get; init; } = HelpIndexBuilder.DefaultCacheDirectory;

    /// <summary>Текущий индекс; <c>null</c> — справка ещё ни разу не загрузилась</summary>
    public HelpIndex? Current => Volatile.Read(ref _current);

    private bool EmbedderConfigured => !string.IsNullOrWhiteSpace(searchOptions.Value.EmbeddingsEndpoint);

    public async Task<HelpSearchResult> Search(string query, CancellationToken ct)
    {
        var index = Current;
        if (index == null)
        {
            metrics?.HelpSearch(false);

            return new HelpSearchResult(null, []);
        }

        var text = HybridSearch.Normalize(query);
        if (text.Length == 0)
        {
            metrics?.HelpSearch(false);

            return new HelpSearchResult(index.Commit, []);
        }

        var vector = await QueryVector(text, ct);
        var hits = index.Search(text, vector, options.Value.VectorThreshold);
        // Доля пустых поисков — список дыр в справке (спец. help, 10)
        metrics?.HelpSearch(hits.Count > 0);

        return new HelpSearchResult(index.Commit, hits);
    }

    /// <summary>
    /// Один проход опроса: прочитать манифест выпуска, и если коммит сменился — собрать
    /// новый индекс рядом со старым и подменить одной ссылкой. Пока строится, ищет
    /// прежний; сборка упала — прежний и остаётся.
    ///
    /// Ссылку <c>current</c> разрешаем один раз в начале: публикация может переключить
    /// её посреди чтения, и манифест одной версии со статьями другой дали бы индекс
    /// с чужим коммитом
    /// </summary>
    public async Task Refresh(CancellationToken ct)
    {
        var directory = Resolve(options.Value.Path);
        var manifest = directory == null ? null : HelpManifest.Read(directory);
        if (directory == null || manifest == null)
        {
            Problem(directory == null
                ? $"Help directory {options.Value.Path} not found, help search answers «unavailable»"
                : $"Help manifest not found in {directory}, help search answers «unavailable»");

            return;
        }

        _loggedProblem = null;
        if (Current?.Commit == manifest.Commit) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = searchOptions.Value;
        var builder = new HelpIndexBuilder(
            scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            settings.EmbeddingsModel + "\n" + settings.EmbeddingsPassagePrefix,
            CacheDirectory,
            logger);
        var index = await builder.Build(directory, manifest.Commit, EmbedderConfigured, ct);

        Volatile.Write(ref _current, index);
        metrics?.HelpVersion(index.Commit);
        logger.LogInformation(
            "Help {Commit} published: {Sections} sections, {Recomputed} vectors recomputed",
            index.Commit, index.Sections, index.RecomputedVectors);
    }

    /// <summary>Подменить индекс готовым — для сверки по справке репозитория</summary>
    public void Use(HelpIndex index) => Volatile.Write(ref _current, index);

    private static string? Resolve(string path)
    {
        if (!Directory.Exists(path)) return null;

        var target = Directory.ResolveLinkTarget(path, true);

        return target?.FullName ?? path;
    }

    /// <summary>Отказ пишем один раз на состояние, а не каждую минуту опроса</summary>
    private void Problem(string message)
    {
        if (_loggedProblem == message) return;

        _loggedProblem = message;
        logger.LogError("{Message}", message);
    }

    private async Task<float[]?> QueryVector(string query, CancellationToken ct)
    {
        if (!EmbedderConfigured) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(QueryVectorTimeout);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var embedder = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var embeddings = await embedder.GenerateAsync([query], EmbeddingPurpose.ForQuery(), timeout.Token);

            return embeddings.Count == 0 ? null : embeddings[0].Vector.ToArray();
        }
        // Отмена самого запроса — не отказ вектора: её пробрасываем как есть
        catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(e, "Help query embedding failed, falling back to keywords only");

            return null;
        }
    }
}
