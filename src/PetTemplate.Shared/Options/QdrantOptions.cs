namespace PetTemplate.Shared.Options;

/// <summary>
/// Адрес Qdrant (спец. search, 3; infrastructure, 6): gRPC-порт <c>6334</c>. Локально —
/// контейнер из корневого <c>docker-compose.yml</c>, на сервере — <c>qdrantcontainer</c>
/// из <c>appsettings.Release.json</c>: адрес соседнего контейнера — не секрет
/// </summary>
public class QdrantOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 6334;
}
