using System.Collections.Concurrent;
using System.Reflection;
using PetTemplate.Tests.Containers;
using Xunit.v3;

[assembly: PetTemplate.Tests.OwnHosts]

namespace PetTemplate.Tests;

/// <summary>
/// Приложения и базы одного теста. Сам по себе хост живёт до конца прогона — вместе
/// со своим DI-контейнером, пулом соединений и фоновыми службами, а база — до конца
/// контейнера. Тестов станут сотни, и к концу прогона приложений было бы столько же
/// (в ASU к концу прогона было больше 500 хостов, и 513-й повис на лимите слежений за файлами macOS)
/// </summary>
internal sealed class TestScope
{
    private readonly ConcurrentBag<IAsyncDisposable> _hosts = [];
    private readonly ConcurrentDictionary<string, Lazy<Task<(string name, string connectionString)>>> _databases = new();

    public void Own(IAsyncDisposable host) => _hosts.Add(host);

    /// <summary>
    /// База теста по её логическому имени (<c>dbName</c> в <see cref="SetupApplicationExt.SetupApplication{TStartup}"/>):
    /// два приложения с одним именем — одна база (зарегистрировался одним, вошёл другим)
    /// </summary>
    public async Task<string> DatabaseAsync(string dbName) =>
        (await _databases.GetOrAdd(dbName, _ => new(PostgresServer.CreateDatabaseAsync)).Value).connectionString;

    public async Task DisposeAsync()
    {
        try
        {
            // Сначала приложения, потом база: иначе их фоновые службы и пулы ещё
            // ходили бы в дропнутую базу и писали отказы в лог
            while (_hosts.TryTake(out var host)) await host.DisposeAsync();
        }
        finally
        {
            foreach (var database in _databases.Values)
            {
                if (database.IsValueCreated) await PostgresServer.DropDatabaseAsync((await database.Value).name);
            }
        }
    }
}

/// <summary>
/// Владелец приложений и баз текущего теста. Заводит его <see cref="OwnHostsAttribute"/>
/// перед тестом, он же и опустошает после
/// </summary>
internal static class TestHosts
{
    /// <summary>
    /// По id теста, а не <c>AsyncLocal</c>: классы идут параллельно, и останавливать
    /// приложения соседа нельзя, а xunit v3 отдаёт текущий тест в любом месте
    /// (<c>TestContext.Current</c>)
    /// </summary>
    private static readonly ConcurrentDictionary<string, TestScope> Scopes = new();

    public static TestScope Current =>
        TestContext.Current.Test is { } test && Scopes.TryGetValue(test.UniqueID, out var scope)
            ? scope
            : throw new InvalidOperationException("Приложение под тест поднимается только внутри теста: владельца ему заводит [assembly: OwnHosts]");

    internal static void Begin(string testId) => Scopes[testId] = new TestScope();

    internal static void EndAndDispose(string testId)
    {
        // Task.Run, потому что ждать остановки хоста прямо тут — это ждать на
        // SynchronizationContext, которым xunit ограничивает параллельность:
        // занятый поток он себе не вернёт, и прогон встанет
        if (Scopes.TryRemove(testId, out var scope)) Task.Run(scope.DisposeAsync).GetAwaiter().GetResult();
    }
}

/// <summary>
/// «До и после каждого теста сборки» — см. <c>[assembly: OwnHosts]</c> в начале
/// файла. Заводить владельца полем в каждом классе — это один и тот же код в каждом
/// файле, и новый класс тестов про него забудет
/// </summary>
internal sealed class OwnHostsAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => TestHosts.Begin(test.UniqueID);

    public override void After(MethodInfo methodUnderTest, IXunitTest test) => TestHosts.EndAndDispose(test.UniqueID);
}
