using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Tests.Arranges;

namespace PetTemplate.Tests;

/// <summary>
/// Проверка на саму обвязку тестов: приложение, поднятое тестом, должно быть
/// остановлено, когда тест закончился (<see cref="TestHosts"/>). Иначе хосты копятся
/// до конца прогона, и прогон упирается в лимит слежений за файлами macOS.
/// <para>
/// Проверяет класс сам за собой: владелец опустошается после теста, а
/// <c>DisposeAsync</c> класса xunit зовёт уже после этого
/// </para>
/// </summary>
public class HostOwnershipTests : IAsyncLifetime
{
    private HttpClient? _viaGiven;
    private HttpClient? _viaFactory;
    private IServiceProvider? _services;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _viaGiven, _viaFactory })
        {
            var stopped = await Assert.ThrowsAnyAsync<ObjectDisposedException>(
                () => client!.GetAsync("/api/Profile/Get"));

            Assert.NotNull(stopped);
        }

        // Провайдер, который SetupApplication собирает вторым, закрывается вместе с хостом
        Assert.Throws<ObjectDisposedException>(() => _services!.CreateScope());
    }

    [Fact]
    public async Task HostsAreDisposedAfterTest()
    {
        var (given, _) = await Given.ApiClient();
        _viaGiven = given;

        // Фабрика, поднятая руками, — такой же хост, и владельца ей находит
        // SetupApplication, а не тест
        var (manual, services) = await new ApiApplicationFactory<Program>()
            .SetupApplication(dbName: Guid.NewGuid().ToString());
        _viaFactory = manual;
        _services = services;

        await given.GetAsync("/api/Profile/Get", TestContext.Current.CancellationToken);
        await manual.GetAsync("/api/Profile/Get", TestContext.Current.CancellationToken);
    }
}
