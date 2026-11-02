using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Search;

/// <summary>
/// Эмбеддер по OpenAI-совместимому <c>POST /embeddings</c> (спец. search, 3). Один на все
/// сущности: считать векторы разным разделам нечем разным.
///
/// Считает на процессоре, в своём контейнере рядом с API (llama.cpp): мелкая модель
/// считает чанк за десятки миллисекунд на обычных ядрах, а векторы не уходят
/// к внешнему поставщику — текст остаётся у нас.
///
/// Префиксы e5 (<c>query: </c> / <c>passage: </c>) ставит сам, по знаку
/// <see cref="EmbeddingPurpose"/> в опциях вызова: в Lad и ASU их не было, шаблон
/// их включил (спец. search, 3; <c>tech-debt/embeddings-model.md</c>)
/// </summary>
public sealed class SearchEmbedder(HttpClient http, IOptions<SearchOptions> settings)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prefix = Prefix(EmbeddingPurpose.Of(options));
        var input = values.Select(x => prefix + x).ToArray();
        if (input.Length == 0) return [];

        var response = await http.PostAsJsonAsync("embeddings", new
        {
            model = settings.Value.EmbeddingsModel,
            input
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");

        // Порядок ответа — порядок запроса: так договорился формат, и по нему чанки
        // раскладываются обратно по своим номерам
        return
        [
            .. data.EnumerateArray().Select(x => new Embedding<float>(
                x.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray()))
        ];
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private string Prefix(string? purpose) => purpose switch
    {
        EmbeddingPurpose.Query => settings.Value.EmbeddingsQueryPrefix,
        EmbeddingPurpose.Passage => settings.Value.EmbeddingsPassagePrefix,
        _ => string.Empty
    };
}
