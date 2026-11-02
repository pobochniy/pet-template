using Grpc.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PetTemplate.Services.Qdrant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Search;

/// <summary>
/// Общее у гибридных поисков (спец. search, 7): векторная половина из Qdrant и слияние
/// выдач по RRF. Ключевая половина у каждой сущности своя — свой <c>tsvector</c>
/// по своим колонкам (<c>FullTextSearch</c>), — а слияние и вектор одни: разделы
/// не должны ранжироваться по-разному. Слиянием пользуется и справка, у которой
/// обе половины в памяти (спец. help, 6.3)
/// </summary>
public static class HybridSearch
{
    /// <summary>
    /// Сглаживающая константа RRF. Шестьдесят — принятое по умолчанию значение:
    /// оно гасит разницу между первым и вторым местом ровно настолько, чтобы
    /// документ, найденный обоими способами, обгонял найденного одним
    /// </summary>
    private const int RrfK = 60;

    /// <summary>Больше человек в выдаче не просмотрит, а ключевой половине каждый лишний — работа</summary>
    public const int MaxResults = 20;

    /// <summary>
    /// Длиннее запрос — не запрос, а вставленный текст: эмбеддер его обрежет, а
    /// полнотекстовая половина будет искать по каждому слову
    /// </summary>
    public const int MaxQueryLength = 200;

    /// <summary>
    /// Сколько ждём вектор. Запрос — одна короткая строка: эмбеддер считает её десятки
    /// миллисекунд, Qdrant отвечает так же. Дольше — значит, контейнер лежит или
    /// захлебнулся, и ждать его незачем
    /// </summary>
    private static readonly TimeSpan VectorTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Запрос, обрезанный по краям и до <see cref="MaxQueryLength"/>; пустой — пустая выдача</summary>
    public static string Normalize(string? query)
    {
        var trimmed = (query ?? string.Empty).Trim();
        return trimmed.Length > MaxQueryLength ? trimmed[..MaxQueryLength] : trimmed;
    }

    /// <summary>
    /// RRF: складываем обратные позиции во всех выдачах. Позиция в каждой выдаче
    /// важнее её абсолютного счёта
    /// </summary>
    public static IReadOnlyDictionary<TKey, double> Merge<TKey>(params IReadOnlyList<TKey>[] rankings)
        where TKey : notnull
    {
        var scores = new Dictionary<TKey, double>();
        foreach (var list in rankings)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                scores[list[rank]] = scores.GetValueOrDefault(list[rank]) + 1d / (RrfK + rank + 1);
            }
        }

        return scores;
    }

    /// <summary>
    /// Слияние и потолок. При равном счёте — больший ключ выше: у ключей <c>Guid</c> v7
    /// это «новее выше», а без правила порядок зависел бы от того, как словарь разложил ключи
    /// </summary>
    public static IReadOnlyList<TKey> Ranked<TKey>(int limit, params IReadOnlyList<TKey>[] rankings)
        where TKey : notnull =>
    [
        .. Merge(rankings)
            .OrderByDescending(x => x.Value)
            .ThenByDescending(x => x.Key)
            .Take(limit)
            .Select(x => x.Key)
    ];

    /// <summary>
    /// Векторная половина. Выдача схлопывается по сущности: лучший чанк даёт ей счёт
    /// и фрагмент. <paramref name="workspaceId"/> — обязательный фильтр: коллекция одна
    /// на всех, и вектор чужого пространства лежит рядом с вашим (спец. search, 6).
    /// Эмбеддер не настроен — половины нет, ищет только ключевая.
    ///
    /// Эмбеддер или Qdrant лежат, не отвечают за <see cref="VectorTimeout"/> — половины
    /// тоже нет, а не ошибки: они живут в отдельных контейнерах, и их отказ не должен
    /// ронять поиск, у которого есть вторая, полнотекстовая половина. Отказ пишется
    /// в лог предупреждением
    /// </summary>
    public static async Task<IReadOnlyList<SearchHit>> Vector(
        VectorQuery query,
        QdrantService qdrant,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        SearchOptions settings,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.EmbeddingsEndpoint)) return [];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(VectorTimeout);

        List<SearchHit> found;
        try
        {
            var embeddings = await embedder.GenerateAsync([query.Text], EmbeddingPurpose.ForQuery(), timeout.Token);
            if (embeddings.Count == 0) return [];

            // Тип в фильтре обязателен: в коллекции лежат и другие сущности (спец. search, 3)
            found = await qdrant.Search(
                settings.EmbeddingsCollection,
                query.WorkspaceId,
                [query.Type],
                embeddings[0].Vector.ToArray(),
                // С запасом: у сущности бывает несколько чанков
                (ulong)(query.Limit * 3),
                timeout.Token,
                settings.VectorMinScore > 0 ? settings.VectorMinScore : null);
        }
        // Отмена самого запроса — не отказ вектора: её пробрасываем как есть
        catch (Exception e) when (!ct.IsCancellationRequested
                                  && e is HttpRequestException or RpcException or OperationCanceledException)
        {
            logger.LogWarning(e, "Vector search for {Type} failed, falling back to full-text only", query.Type);
            return [];
        }

        return
        [
            .. found
                .GroupBy(x => x.EntityId)
                .Select(x => x.OrderByDescending(c => c.Score).First())
                .OrderByDescending(x => x.Score)
                .Take(query.Limit)
        ];
    }
}
