using Qdrant.Client;

namespace PetTemplate.Services.Qdrant;

/// <summary>
/// Миграция коллекции Qdrant: заводит её на старте приложения, как миграция EF заводит
/// таблицу. Применяет их <c>QdrantInitializer</c> — и только те, коллекции
/// которых на сервере ещё нет
/// </summary>
public interface IQdrantMigration
{
    string CollectionName { get; }

    Task InitializeCollection(QdrantClient client, CancellationToken ct = default);
}
