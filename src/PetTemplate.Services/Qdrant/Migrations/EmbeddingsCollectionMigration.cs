using Microsoft.Extensions.Options;
using PetTemplate.Shared.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace PetTemplate.Services.Qdrant.Migrations;

/// <summary>
/// Коллекция под модель эмбеддингов (спец. search, 3). Она одна: в ней лежат все типы
/// сущностей, а различаются они полем <c>type</c> в payload.
///
/// Имя и размерность приходят из конфигурации, а не константами: размерность у каждой
/// модели своя, а имя привязано к модели (<c>pettemplate_e5_small_384</c>) — несравнимые
/// векторы разных эмбеддеров в одной коллекции заставили бы поиск тихо врать. Отсюда же
/// и способ сменить модель: не «мигрировать» старую коллекцию, а завести рядом новую
/// под новым именем и прогнать полный реиндекс
/// </summary>
public class EmbeddingsCollectionMigration(IOptions<SearchOptions> options) : IQdrantMigration
{
    public string CollectionName => options.Value.EmbeddingsCollection;

    public async Task InitializeCollection(QdrantClient client, CancellationToken ct = default)
    {
        await client.CreateCollectionAsync(
            CollectionName,
            new VectorParams
            {
                Size = (ulong)options.Value.EmbeddingsDimension,
                Distance = QdrantConst.DistanceAlgorithm
            },
            cancellationToken: ct);

        // Payload-индексы заводятся вместе с коллекцией: фильтр без индекса
        // на большой коллекции — полный проход
        foreach (var key in (string[])[QdrantConst.WorkspaceKey, QdrantConst.EntityKey, QdrantConst.TypeKey])
        {
            await client.CreatePayloadIndexAsync(CollectionName, key, PayloadSchemaType.Keyword,
                cancellationToken: ct);
        }
    }
}
