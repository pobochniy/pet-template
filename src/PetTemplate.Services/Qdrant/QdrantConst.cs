using Qdrant.Client.Grpc;

namespace PetTemplate.Services.Qdrant;

public static class QdrantConst
{
    /// <summary>
    /// Мера близости у всех наших коллекций одна. Размерность вектора константой
    /// не задаётся: она у каждой модели эмбеддингов своя и живёт в конфигурации
    /// рядом с именем модели (спец. search, 3)
    /// </summary>
    public const Distance DistanceAlgorithm = Distance.Cosine;

    /// <summary>Ключ payload, по которому и фильтруем, и строим индекс (спец. search, 6)</summary>
    public const string WorkspaceKey = "workspaceId";

    /// <summary>Тип сущности: по нему фильтрует поиск, иначе «среди задач» принесёт заметки</summary>
    public const string TypeKey = "type";

    public const string EntityKey = "entityId";

    public const string ChunkKey = "chunk";

    public const string TextKey = "text";
}
