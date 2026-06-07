using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using PetTemplate.Db.Entity;

namespace PetTemplate.Tests.Arranges;

public static class Given
{
    public static async Task<(HttpClient client, ApplicationContext db)> ApiClient(
        Action<ApplicationContext>? dbArrange = null,
        string? token = null,
        string? mySqlConnectionString = null)
    {
        // Используем разные factory в зависимости от типа БД
        var factory = new ApiApplicationFactory<Program>();

        var (client, serviceProvider) = await factory
            .SetupApplication(
                dbArrange: dbArrange,
                configureSettings: TestSettings.ConfigureAuth(),
                bearerToken: token,
                dbName: Guid.NewGuid().ToString());
        var db = serviceProvider.GetRequiredService<ApplicationContext>();
        return (client, db);
    }
}