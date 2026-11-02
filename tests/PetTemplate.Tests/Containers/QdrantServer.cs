using Microsoft.Extensions.Options;
using PetTemplate.Services.Qdrant.Migrations;
using PetTemplate.Shared.Options;
using Qdrant.Client;
using Testcontainers.Qdrant;

namespace PetTemplate.Tests.Containers;

/// <summary>
/// Один Qdrant на всю тестовую сборку — по образцу <see cref="PostgresServer"/>: контейнер
/// поднимается на первом обращении, живёт до конца прогона, уносит его Ryuk.
/// <para>
/// Изоляцию тестам даёт не отдельный сервер, а отдельная коллекция
/// (<see cref="CreateCollectionAsync"/>): у каждого теста своё имя, и точки соседнего
/// теста в его выдачу не попадут, даже если фильтр по пространству однажды сломают —
/// это ловит свой тест, а не случайность соседей. Коллекцию заводит та же миграция,
/// что и в бою: обработчик индексации кладёт точки в готовую коллекцию и создавать
/// её не пробует
/// </para>
/// </summary>
internal static class QdrantServer
{
    /// <summary>gRPC — им ходит <c>QdrantClient</c></summary>
    private const int GrpcPort = 6334;

    private static readonly Lazy<Task<QdrantContainer>> Server = new(StartAsync);

    public static async Task<QdrantOptions> OptionsAsync()
    {
        var server = await Server.Value;

        return new QdrantOptions { Host = server.Hostname, Port = server.GetMappedPublicPort(GrpcPort) };
    }

    /// <summary>Своя коллекция под тест с векторами размерности <paramref name="dimension"/>; её имя</summary>
    public static async Task<string> CreateCollectionAsync(int dimension)
    {
        var options = await OptionsAsync();
        var name = $"pettemplate_test_{Guid.NewGuid():N}";

        using var client = new QdrantClient(options.Host, options.Port);
        await new EmbeddingsCollectionMigration(Options.Create(new SearchOptions
        {
            EmbeddingsCollection = name,
            EmbeddingsDimension = dimension
        })).InitializeCollection(client);

        return name;
    }

    private static async Task<QdrantContainer> StartAsync()
    {
        // Версия — как в docker-compose.yml и на сервере (спец. infrastructure, 8)
        var container = new QdrantBuilder("qdrant/qdrant:v1.20.0")
            .WithName($"pettemplate-test-qdrant-{Guid.NewGuid().ToString("N")[..8]}")
            // Точки живут один прогон — хранилище в памяти, как данные тестового Postgres
            .WithTmpfsMount("/qdrant/storage")
            .Build();

        await container.StartAsync();
        return container;
    }
}
