using Microsoft.Extensions.Options;
using PetTemplate.Services.Qdrant;
using PetTemplate.Shared.Options;
using Qdrant.Client;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Миграции Qdrant: коллекции, которых на сервере ещё нет, заводятся на старте
/// приложения (спец. search, 3). Сами миграции приходят регистрацией
/// <see cref="IQdrantMigration"/> — добавить коллекцию значит добавить класс,
/// а не тронуть этот.
///
/// Коллекция заводится <b>здесь и только здесь</b>: обработчик индексации кладёт точки
/// в готовую коллекцию и создавать её не пробует — иначе «как устроена коллекция»
/// жило бы в двух местах и однажды разъехалось бы размерностью или набором индексов.
///
/// Уже существующую коллекцию не трогаем: у Qdrant нет ни ALTER, ни истории применённых
/// миграций, а размерность у готовой коллекции не меняется в принципе. Смена модели
/// эмбеддингов — это новое имя коллекции и полный реиндекс.
///
/// Работа — в <c>StartAsync</c>: Qdrant обязателен, как база (спец. infrastructure, 6),
/// и без него API не поднимается. В Lad здесь фоновая служба с повторами — шаблон,
/// как и ASU, решил иначе
/// </summary>
public sealed class QdrantInitializer(
    IOptions<QdrantOptions> qdrantOptions,
    IEnumerable<IQdrantMigration> migrations,
    ILogger<QdrantInitializer> logger) : IHostedService, IDisposable
{
    private readonly QdrantClient _client =
        new(host: qdrantOptions.Value.Host, port: qdrantOptions.Value.Port);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Имя пустое — коллекции у этой сборки нет вовсе
        foreach (var migration in migrations.Where(x => !string.IsNullOrWhiteSpace(x.CollectionName)))
        {
            if (await _client.CollectionExistsAsync(migration.CollectionName, cancellationToken)) continue;

            await migration.InitializeCollection(_client, cancellationToken);

            logger.LogInformation("Qdrant collection {Collection} created", migration.CollectionName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
