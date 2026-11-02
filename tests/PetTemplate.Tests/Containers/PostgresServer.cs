using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetTemplate.Db.Entity;
using Testcontainers.PostgreSql;

namespace PetTemplate.Tests.Containers;

/// <summary>
/// Один сервер PostgreSQL на всю тестовую сборку: контейнер поднимается на первом
/// обращении и живёт до конца прогона. Изоляцию тестам даёт не отдельный сервер
/// каждому, а отдельная база (<see cref="CreateDatabaseAsync"/>), которую после теста
/// дропает <see cref="TestHosts"/>.
/// <para>
/// Останавливать контейнер некому: <c>ICollectionFixture</c> отнял бы у классов
/// параллельность и съел весь выигрыш. Контейнер уносит Ryuk из Testcontainers, когда
/// процесс тестов завершается — на этом же механизме держится уборка при падении прогона
/// </para>
/// </summary>
internal static class PostgresServer
{
    /// <summary>
    /// База со схемой, с которой копируются базы тестов (<c>CREATE DATABASE … TEMPLATE</c>):
    /// копия — это файлы, а не десятки DDL-команд <c>EnsureCreated</c> на каждый тест
    /// (<c>tech-debt/test-schema-per-test.md</c>). Схема раскатывается в неё один раз на прогон
    /// </summary>
    private const string TemplateDatabase = "pettemplate_template";

    /// <summary>
    /// <c>Lazy</c> сам разруливает гонку: классы тестов идут параллельно и приходят
    /// сюда одновременно, а стартовать контейнер надо один раз
    /// </summary>
    private static readonly Lazy<Task<PostgreSqlContainer>> Server = new(StartAsync);

    /// <summary>База под тест со схемой из шаблона: её имя и строка подключения к ней</summary>
    public static async Task<(string name, string connectionString)> CreateDatabaseAsync()
    {
        var server = await Server.Value;
        var name = $"pettemplate_test_{Guid.NewGuid():N}";

        await ExecuteAsync(server.GetConnectionString(), $"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabase}\"");

        return (name, ConnectionStringTo(server, name));
    }

    public static async Task DropDatabaseAsync(string name)
    {
        var server = await Server.Value;

        // Пул Npgsql — статический на строку подключения и переживает остановленный хост:
        // его соединения сначала закрываем у себя, а оставшиеся сервер рвёт сам (FORCE)
        await using (var connection = new NpgsqlConnection(ConnectionStringTo(server, name)))
        {
            NpgsqlConnection.ClearPool(connection);
        }

        await ExecuteAsync(server.GetConnectionString(), $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        // Версия — как в docker-compose.yml и на сервере (спец. infrastructure)
        var container = new PostgreSqlBuilder("postgres:17")
            .WithName($"pettemplate-test-postgres-{Guid.NewGuid().ToString("N")[..8]}")
            .WithDatabase("pettemplatedb")
            // Данные живут один прогон: каталог данных в памяти и без сброса на диск.
            // Каждая команда схемы и каждый коммит в Postgres по умолчанию ждут fsync,
            // а надёжность после сбоя тестовой базе не нужна — аналог «MySQL в tmpfs без fsync» у Lad и ASU
            .WithTmpfsMount("/var/lib/postgresql/data")
            .WithCommand(
                "-c", "fsync=off",
                "-c", "synchronous_commit=off",
                "-c", "full_page_writes=off",
                // Сервер один на всю сборку, а у каждой базы теста свой пул соединений:
                // дефолтных 100 на параллельные классы не хватит
                "-c", "max_connections=500")
            .Build();

        await container.StartAsync();
        await CreateTemplateAsync(container);
        return container;
    }

    /// <summary>
    /// Схема раскатывается тем же приложением, что и в тестах, — с его настройками
    /// контекста (snake_case, расширения), а не их копией здесь. До пересборки <c>Init</c>
    /// это <c>EnsureCreated</c>; после — <c>Migrate()</c>, чтобы прогон ловил сломанную миграцию
    /// (<c>src/PetTemplate.Db/README.md</c>, «Первая миграция Init»)
    /// </summary>
    private static async Task CreateTemplateAsync(PostgreSqlContainer server)
    {
        await ExecuteAsync(server.GetConnectionString(), $"CREATE DATABASE \"{TemplateDatabase}\"");

        var connectionString = ConnectionStringTo(server, TemplateDatabase);
        await using (var factory = new ApiApplicationFactory<Program>().WithWebHostBuilder(builder =>
                         builder.UseSetting("ConnectionStrings:AppConnection", connectionString)))
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            await db.Database.EnsureCreatedAsync();
        }

        // Копировать базу можно, только пока к ней никто не подключён: пул после
        // раскатки держал бы соединение, и первый же CREATE DATABASE … TEMPLATE упал бы
        await using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    private static string ConnectionStringTo(PostgreSqlContainer server, string database) =>
        new NpgsqlConnectionStringBuilder(server.GetConnectionString()) { Database = database }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
