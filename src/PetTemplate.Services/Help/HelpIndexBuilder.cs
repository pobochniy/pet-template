using System.Security.Cryptography;
using System.Text;
using PetTemplate.Help;
using PetTemplate.Services.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PetTemplate.Services.Help;

/// <summary>
/// Сборка индекса справки из каталога выпуска (спец. help, 6.1–6.2): статьи →
/// разделы → чанки → векторы. Векторы кэшируются на диске контейнера по хэшу текста
/// чанка и имени модели: при новой версии справки пересчитываются только изменившиеся
/// разделы, а рестарт не стоит полутора сотен запросов к эмбеддеру
/// </summary>
/// <param name="embeddingsModel">
/// Часть ключа кэша: имя модели и префикс документа (<see cref="EmbeddingPurpose.ForPassage"/>).
/// Сменили любое — векторы прежнего ключа несравнимы с новыми и считаются заново
/// </param>
public class HelpIndexBuilder(
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    string embeddingsModel,
    string? cacheDirectory,
    ILogger logger)
{
    /// <summary>
    /// Кэш по умолчанию — во временном каталоге контейнера, вне смонтированной справки:
    /// та смонтирована только на чтение. Новая выкатка — новый контейнер и полный
    /// пересчёт, это секунды
    /// </summary>
    public static readonly string DefaultCacheDirectory = Path.Combine(Path.GetTempPath(), "pettemplate-help-vectors");

    /// <summary>Сколько чанков в одном запросе к эмбеддеру</summary>
    private const int Batch = 16;

    /// <summary>
    /// Сколько ждём один запрос к эмбеддеру. Дольше — он лежит или захлебнулся, и
    /// держать сборку новой версии на сотне секунд таймаута клиента незачем
    /// </summary>
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Граница страховочной нарезки: раздел длиннее неё линтер не пропускает, и в
    /// нормальной работе <see cref="SearchChunker"/> не режет ничего (3.3)
    /// </summary>
    private const int SectionTokens = 350;

    public async Task<HelpIndex> Build(string directory, string commit, bool withVectors, CancellationToken ct)
    {
        var chunks = new List<(Guid Id, string Text, HelpHit Hit)>();
        foreach (var file in Directory.GetFiles(directory, "*.md").Where(x => !Path.GetFileName(x).StartsWith('_'))
                     .Order(StringComparer.Ordinal))
        {
            var article = HelpArticleParser.Parse(Path.GetFileNameWithoutExtension(file), await File.ReadAllTextAsync(file, ct));
            foreach (var section in article.Sections)
            {
                var id = HelpIndex.SectionId(article.Name, section.Title);
                var hit = new HelpHit(article.Title, section.Title, section.Body, article.Page);
                var text = section.ChunkText(article.Title);

                if (HelpArticleParser.Tokens(text) <= SectionTokens)
                    chunks.Add((id, text, hit));
                else
                    chunks.AddRange(SearchChunker.Split(text, SectionTokens).Select(x => (id, x.Text, hit)));
            }
        }

        var (vectors, recomputed) = withVectors
            ? await Vectors(chunks.Select(x => x.Text).ToList(), ct)
            : (new float[]?[chunks.Count], 0);

        return new HelpIndex(
            commit,
            [.. chunks.Select((x, i) => new HelpChunk(x.Id, x.Text, x.Hit, HelpTerms.Of(x.Text), vectors[i]))],
            recomputed);
    }

    /// <summary>
    /// Векторы чанков: из кэша, недостающие — у эмбеддера. Эмбеддер лежит — индекс
    /// собирается без векторов у недостающих, ищет ключевая половина (6.3)
    /// </summary>
    private async Task<(float[]?[] Vectors, int Recomputed)> Vectors(List<string> texts, CancellationToken ct)
    {
        var vectors = new float[]?[texts.Count];
        var missing = new List<int>();
        for (var i = 0; i < texts.Count; i++)
        {
            vectors[i] = ReadCache(texts[i]);
            if (vectors[i] == null) missing.Add(i);
        }

        var recomputed = 0;
        try
        {
            foreach (var batch in missing.Chunk(Batch))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(BatchTimeout);
                var embeddings = await embedder.GenerateAsync(
                    batch.Select(x => texts[x]), EmbeddingPurpose.ForPassage(), timeout.Token);
                if (embeddings.Count != batch.Length) break; // эмбеддер не настроен

                for (var j = 0; j < batch.Length; j++)
                {
                    vectors[batch[j]] = embeddings[j].Vector.ToArray();
                    WriteCache(texts[batch[j]], vectors[batch[j]]!);
                    recomputed++;
                }
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(e, "Help embeddings failed, {Missing} sections are searchable by keywords only",
                missing.Count - recomputed);
        }

        return (vectors, recomputed);
    }

    private string? CachePath(string text) =>
        cacheDirectory == null
            ? null
            : Path.Combine(cacheDirectory,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(embeddingsModel + "\n" + text))) + ".bin");

    private float[]? ReadCache(string text)
    {
        var path = CachePath(text);
        if (path == null || !File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);

            return bytes.Length % sizeof(float) == 0 && bytes.Length > 0
                ? System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes).ToArray()
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Кэш — ускорение, а не данные: не записался — посчитаем в следующий раз</summary>
    private void WriteCache(string text, float[] vector)
    {
        var path = CachePath(text);
        if (path == null) return;

        try
        {
            Directory.CreateDirectory(cacheDirectory!);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, System.Runtime.InteropServices.MemoryMarshal.AsBytes(vector.AsSpan()).ToArray());
            File.Move(temp, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not cache help vector at {Path}", path);
        }
    }
}
