using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Db.Entity;

namespace PetTemplate.Tests.Features.Health;

/// <summary>
/// Кольцо ключей Data Protection живёт в базе (спец. infrastructure, 3.1): защищённое одним
/// экземпляром API читается другим — так же переживается выкатка, пересоздающая контейнер
/// </summary>
public class KeyRingTests
{
    private const string Purpose = "key-ring-test";

    [Fact]
    public async Task ProtectedByOneHostIsReadByAnother()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, first) = await new ApiApplicationFactory<Program>()
            .SetupApplication(dbName: dbName, configureSettings: TestSettings.ConfigureAuth());
        var secret = first.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("Рога и Копыта");

        var (_, second) = await new ApiApplicationFactory<Program>()
            .SetupApplication(dbName: dbName, configureSettings: TestSettings.ConfigureAuth());
        var plain = second.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Unprotect(secret);

        Assert.Equal("Рога и Копыта", plain);
    }

    [Fact]
    public async Task KeyIsStoredInDatabase()
    {
        var (_, services) = await new ApiApplicationFactory<Program>()
            .SetupApplication(dbName: Guid.NewGuid().ToString(), configureSettings: TestSettings.ConfigureAuth());
        services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("Шарики и Палочки");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        Assert.True(await db.DataProtectionKeys.AnyAsync(TestContext.Current.CancellationToken));
    }
}
