using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Services.Search;

namespace PetTemplate.Tests.Mocks;

/// <summary>
/// Эмбеддер без модели (спец. search, 3): один и тот же вектор на любой текст. Тесты
/// поиска проверяют фильтры, жизненный цикл точки и слияние, а не качество близости —
/// эмбеддер здесь заведомо глупый. Одинаковые векторы нарочно: без фильтра по
/// пространству Qdrant вернул бы обе точки с равным счётом
/// </summary>
public sealed class FakeEmbedder : IEmbeddingGenerator<string, Embedding<float>>
{
    public static readonly float[] Vector = [1, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Что спрашивали и зачем (<see cref="EmbeddingPurpose"/>) — для проверки индексации</summary>
    public ConcurrentQueue<(string Text, string? Purpose)> Calls { get; } = new();

    /// <summary>Контейнер «лёг»: вызов бросает, как упавший HTTP</summary>
    public bool IsDown { get; set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDown) throw new HttpRequestException("Mock: connection refused");

        var input = values.ToArray();
        foreach (var text in input) Calls.Enqueue((text, EmbeddingPurpose.Of(options)));

        return Task.FromResult<GeneratedEmbeddings<Embedding<float>>>(
            [.. input.Select(_ => new Embedding<float>(Vector))]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Держать нечего
    }

    public Action<IServiceCollection> Services() =>
        services => services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(this);
}
