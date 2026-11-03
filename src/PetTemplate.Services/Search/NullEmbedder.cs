using Microsoft.Extensions.AI;

namespace PetTemplate.Services.Search;

/// <summary>
/// Эмбеддера нет: адрес не настроен либо это тест. Векторов не отдаёт, и индексация
/// на нём тихо не делается — поиск тогда находит только по словам, а остальное
/// работает. Отдельного состояния «индексируется» в интерфейсе нет
/// </summary>
public sealed class NullEmbedder : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<GeneratedEmbeddings<Embedding<float>>>([]);

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
