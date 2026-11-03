using System.Net;
using PetTemplate.Tests.Containers;

namespace PetTemplate.Tests.Features.Health;

/// <summary>
/// <c>GET /health</c> (спец. infrastructure, 6): по нему выкатка решает, оставить новый
/// контейнер или вернуть прежний. Без токена — его спрашивает скрипт выкатки
/// </summary>
public class HealthApiTests
{
    [Fact]
    public async Task IsHealthyWithDatabaseAndQdrant()
    {
        var qdrant = await QdrantServer.OptionsAsync();
        var (client, _) = await new ApiApplicationFactory<Program>().SetupApplication(
            dbName: Guid.NewGuid().ToString(),
            configureSettings: settings =>
            {
                settings["QdrantOptions:Host"] = qdrant.Host;
                settings["QdrantOptions:Port"] = qdrant.Port.ToString();
            });

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Qdrant обязателен, как база: недостижим — 503, и выкатка откатывается</summary>
    [Fact]
    public async Task IsUnhealthyWithoutQdrant()
    {
        var (client, _) = await new ApiApplicationFactory<Program>().SetupApplication(
            dbName: Guid.NewGuid().ToString(),
            configureSettings: settings =>
            {
                // Порт, на котором никого нет: 1 — привилегированный, Qdrant на нём не живёт
                settings["QdrantOptions:Host"] = "127.0.0.1";
                settings["QdrantOptions:Port"] = "1";
            });

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
