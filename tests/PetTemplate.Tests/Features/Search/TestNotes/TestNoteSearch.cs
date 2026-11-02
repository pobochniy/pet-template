using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Extensions;
using PetTemplate.Services.Qdrant;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Options;

namespace PetTemplate.Tests.Features.Search.TestNotes;

/// <summary>
/// Гибридный поиск по тестовой сущности (спец. search, 7 и 9) — образец того, что раздел
/// проекта пишет к своему типу: векторная половина — общая (<see cref="HybridSearch.Vector"/>),
/// полнотекстовая — своя, по своей колонке <c>tsvector</c>; обе — с фильтром по пространству;
/// слияние — общий RRF (<see cref="HybridSearch.Ranked{TKey}"/>). Отдаёт идентификаторы
/// по убыванию счёта; права проверяет контроллер раздела, сущности достаёт его сервис
/// </summary>
public class TestNoteSearch(
    TestNotesContext db,
    QdrantService qdrant,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    IOptions<SearchOptions> options,
    ILogger<TestNoteSearch> logger)
{
    public async Task<IReadOnlyList<Guid>> Find(Guid workspaceId, string query, CancellationToken ct)
    {
        query = HybridSearch.Normalize(query);
        if (query.Length == 0) return [];

        var byVector = await HybridSearch.Vector(
            new VectorQuery(workspaceId, TestNoteSource.NoteType, query, HybridSearch.MaxResults),
            qdrant, embedder, options.Value, logger, ct);
        var byWords = await Words(workspaceId, query, ct);

        return HybridSearch.Ranked(
            HybridSearch.MaxResults,
            [.. byVector.Select(x => Guid.Parse(x.EntityId))],
            byWords);
    }

    /// <summary>
    /// Полнотекстовая половина: «любое из слов» по <c>tsvector</c>, порядок — <c>ts_rank</c>.
    /// Порядок обязателен: RRF считает очки по месту в выдаче, а без <c>ORDER BY</c> первые
    /// и их порядок — случайность плана запроса
    /// </summary>
    public async Task<IReadOnlyList<Guid>> Words(Guid workspaceId, string query, CancellationToken ct)
    {
        var words = FullTextSearch.AnyWord(query);

        return await db.Notes
            .Where(x => x.WorkspaceId == workspaceId)
            .Where(x => x.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, words)))
            .OrderByDescending(x => x.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, words)))
            .ThenByDescending(x => x.Id)
            .Select(x => x.Id)
            .Take(HybridSearch.MaxResults)
            .ToListAsync(ct);
    }
}
