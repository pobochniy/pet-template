using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Options;

namespace PetTemplate.Tests.Features.Search;

/// <summary>
/// Префиксы e5 (спец. search, 3): запрос уходит эмбеддеру с <c>query: </c>, чанки —
/// с <c>passage: </c>, вызов без знака — как есть. Эмбеддер — заглушка HTTP, сеть не нужна
/// </summary>
public class SearchEmbedderUnitTests
{
    [Fact]
    public async Task QueryGetsQueryPrefix()
    {
        // Arrange
        var (embedder, sent) = Embedder(new SearchOptions());

        // Act
        await embedder.GenerateAsync(["кран"], EmbeddingPurpose.ForQuery());

        // Assert
        Assert.Equal(["query: кран"], sent.Single());
    }

    [Fact]
    public async Task PassagesGetPassagePrefix()
    {
        // Arrange
        var (embedder, sent) = Embedder(new SearchOptions());

        // Act
        await embedder.GenerateAsync(["# Кран", "Течёт"], EmbeddingPurpose.ForPassage());

        // Assert
        Assert.Equal(["passage: # Кран", "passage: Течёт"], sent.Single());
    }

    /// <summary>Модель без префиксов (bge-m3) — пустые строки в настройках, текст уходит как есть</summary>
    [Fact]
    public async Task EmptyPrefixesSendTextAsIs()
    {
        // Arrange
        var (embedder, sent) = Embedder(new SearchOptions { EmbeddingsQueryPrefix = "", EmbeddingsPassagePrefix = "" });

        // Act
        await embedder.GenerateAsync(["кран"], EmbeddingPurpose.ForQuery());

        // Assert
        Assert.Equal(["кран"], sent.Single());
    }

    [Fact]
    public async Task CallWithoutPurposeHasNoPrefix()
    {
        // Arrange
        var (embedder, sent) = Embedder(new SearchOptions());

        // Act
        await embedder.GenerateAsync(["кран"]);

        // Assert
        Assert.Equal(["кран"], sent.Single());
    }

    /// <summary>Векторы раскладываются в порядке запроса — по нему чанки находят свои номера</summary>
    [Fact]
    public async Task KeepsOrderOfInput()
    {
        // Arrange
        var (embedder, _) = Embedder(new SearchOptions());

        // Act
        var vectors = await embedder.GenerateAsync(["первый", "второй"]);

        // Assert
        Assert.Equal([0f, 1f], vectors.Select(x => x.Vector.Span[0]));
    }

    private static (SearchEmbedder embedder, List<string[]> sent) Embedder(SearchOptions options)
    {
        var handler = new EmbeddingsHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://embeddings.test/v1/") };

        return (new SearchEmbedder(http, Options.Create(options)), handler.Sent);
    }

    /// <summary>
    /// OpenAI-совместимый <c>POST /embeddings</c>: запоминает <c>input</c>, отвечает вектором
    /// с номером строки в первой координате
    /// </summary>
    private sealed class EmbeddingsHandler : HttpMessageHandler
    {
        public List<string[]> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var input = body.RootElement.GetProperty("input").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Sent.Add(input);

            var data = input.Select((_, i) => new { embedding = new[] { (float)i, 1f } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data }), Encoding.UTF8, "application/json")
            };
        }
    }
}
