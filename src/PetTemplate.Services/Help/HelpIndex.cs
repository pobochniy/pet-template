using System.Security.Cryptography;
using System.Text;
using PetTemplate.Help;
using PetTemplate.Services.Search;

namespace PetTemplate.Services.Help;

/// <summary>Найденный раздел справки — то, что получит инструмент ассистента <c>search_help</c> (спец. help, 6.5)</summary>
public record HelpHit(string Article, string Section, string Text, string? Page);

/// <summary>
/// Статья для чтения человеком (спец. help, 6.5): имя файла, название и разделы markdown-текстом —
/// без фронтматтера и без строк «Как спрашивают», они только для поиска
/// </summary>
public record HelpPage(string Slug, string Title, string Markdown);

/// <summary>
/// Чанк индекса: раздел целиком (6.2) или, если раздел всё-таки длиннее границы,
/// его кусок из <see cref="SearchChunker"/>. Куски одного раздела делят
/// <see cref="SectionId"/>, и выдача схлопывается по разделу
/// </summary>
public record HelpChunk(Guid SectionId, string Text, HelpHit Hit, IReadOnlyList<string> Terms, float[]? Vector);

/// <summary>
/// Индекс справки в памяти (спец. help, 6.1). Неизменяемый: новая версия строится
/// рядом и подменяет старую одной ссылкой, а поиск во время пересборки отвечает прежней.
///
/// Не в Qdrant: справка общая для всех и пространства не знает, а поиск по общей
/// коллекции без фильтра по пространству — ровно та дверь, которую закрывает
/// спец. search, 6. Полторы сотни векторов — доли мегабайта и косинус перебором
/// </summary>
public class HelpIndex
{
    /// <summary>Сколько разделов отдаёт поиск (6.3)</summary>
    public const int MaxHits = 3;

    /// <summary>
    /// Потолок выдачи в токенах (6.3): своя модель с небольшим контекстом не должна
    /// получать пол-справки на каждый вопрос. Первый раздел отдаётся всегда
    /// </summary>
    public const int MaxHitTokens = 1_200;

    /// <summary>Сколько мест каждой половины идёт в слияние</summary>
    private const int Candidates = 10;

    // BM25 — принятые по умолчанию параметры: насыщение частоты и поправка на длину
    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly IReadOnlyList<HelpChunk> _chunks;
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.Ordinal);
    private readonly double _averageLength;

    public HelpIndex(string commit, IReadOnlyList<HelpChunk> chunks, int recomputedVectors,
        IReadOnlyList<HelpPage>? pages = null)
    {
        Commit = commit;
        _chunks = chunks;
        RecomputedVectors = recomputedVectors;
        Pages = pages ?? [];

        foreach (var term in chunks.SelectMany(x => x.Terms.Distinct()))
            _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;

        _averageLength = chunks.Count == 0 ? 1 : Math.Max(1, chunks.Average(x => x.Terms.Count));
    }

    /// <summary>Коммит справки, на которой ищем, — в трассу и метрику версии (10)</summary>
    public string Commit { get; }

    public int Sections => _chunks.Select(x => x.SectionId).Distinct().Count();

    /// <summary>Сколько векторов пришлось посчитать при сборке, а не взять из кэша</summary>
    public int RecomputedVectors { get; }

    public IReadOnlyList<HelpChunk> Chunks => _chunks;

    /// <summary>Статьи выпуска целиком — для страницы справки на сайте (6.5), по имени файла</summary>
    public IReadOnlyList<HelpPage> Pages { get; }

    /// <summary>Сколько чанков встало без вектора — эмбеддер лежал при сборке (6.4)</summary>
    public int MissingVectors => _chunks.Count(x => x.Vector == null);

    /// <summary>
    /// Гибрид (6.3): BM25 и косинус, слияние по RRF тем же <see cref="HybridSearch.Ranked{TKey}"/>,
    /// что у поиска разделов (спец. search, 7). <paramref name="queryVector"/> нет (эмбеддер
    /// не настроен или лежит) — ищет одна ключевая половина
    /// </summary>
    public List<HelpHit> Search(string query, float[]? queryVector, float vectorThreshold)
    {
        var byWords = Keyword(HelpTerms.Of(query));
        List<Guid> byVector = queryVector == null ? [] : Vector(queryVector, vectorThreshold);
        var ranked = HybridSearch.Ranked(Candidates * 2, byVector, byWords);

        var hits = new List<HelpHit>();
        var tokens = 0;
        foreach (var id in ranked)
        {
            var hit = _chunks.First(x => x.SectionId == id).Hit;
            var size = HelpArticleParser.Tokens(hit.Text);
            if (hits.Count > 0 && tokens + size > MaxHitTokens) break;

            hits.Add(hit);
            tokens += size;
            if (hits.Count == MaxHits) break;
        }

        return hits;
    }

    /// <summary>
    /// Постоянный идентификатор раздела — от пары «статья + заголовок», а не от текста:
    /// правка тела не меняет ключ. Пару делает однозначной линтер — заголовки в статье
    /// не повторяются
    /// </summary>
    public static Guid SectionId(string article, string section) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(article + "\n" + section)).AsSpan(0, 16));

    private List<Guid> Keyword(List<string> query)
    {
        if (query.Count == 0) return [];

        var terms = query.Distinct().ToList();
        var n = _chunks.Count;

        return Collapse(_chunks
            .Select(chunk =>
            {
                var length = chunk.Terms.Count;
                var score = 0d;
                foreach (var term in terms)
                {
                    var frequency = chunk.Terms.Count(x => x == term);
                    if (frequency == 0) continue;

                    var df = _documentFrequency[term];
                    var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
                    score += idf * frequency * (K1 + 1) / (frequency + K1 * (1 - B + B * length / _averageLength));
                }

                return (chunk.SectionId, Score: score);
            })
            .Where(x => x.Score > 0));
    }

    private List<Guid> Vector(float[] query, float threshold) =>
        Collapse(_chunks
            .Where(x => x.Vector != null)
            .Select(x => (x.SectionId, Score: (double)Cosine(query, x.Vector!)))
            .Where(x => threshold <= 0 || x.Score >= threshold));

    /// <summary>Лучший кусок даёт место разделу: раздел в выдаче один раз</summary>
    private static List<Guid> Collapse(IEnumerable<(Guid SectionId, double Score)> scored) =>
    [
        .. scored
            .GroupBy(x => x.SectionId)
            .Select(x => (Id: x.Key, Score: x.Max(s => s.Score)))
            .OrderByDescending(x => x.Score)
            .Take(Candidates)
            .Select(x => x.Id)
    ];

    private static float Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na <= 0 || nb <= 0 ? 0 : (float)(dot / Math.Sqrt(na * nb));
    }
}
