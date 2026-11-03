using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Контекст для <c>dotnet ef</c> и бандла миграций — без хоста API. Бандл накатывает
/// миграции на шаге выкатки под владельцем базы (спец. infrastructure, 8): поднимать
/// ради этого всё приложение значило бы требовать от шага ключи подписи, почту
/// и остальные секреты API, которых у него нет и быть не должно.
///
/// Строка подключения — та же переменная, что у API (<c>ConnectionStrings__AppConnection</c>),
/// или <c>--connection</c> бандла. Без неё — дев-база из корневого <c>docker-compose.yml</c>,
/// та же строка, что в <c>appsettings.Development.json</c>: <c>dotnet ef database update</c>
/// на своей машине работает без настройки. Поменяли порт дев-базы — здесь тоже
/// </summary>
public class ApplicationContextFactory : IDesignTimeDbContextFactory<ApplicationContext>
{
    private const string ConnectionVariable = "ConnectionStrings__AppConnection";
    private const string DevelopmentConnection =
        "Host=localhost;Port=5432;Database=pettemplatedb;Username=postgres;Password=1234";

    public ApplicationContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        var builder = new DbContextOptionsBuilder<ApplicationContext>();
        Configure(builder, string.IsNullOrEmpty(connectionString) ? DevelopmentConnection : connectionString);

        return new ApplicationContext(builder.Options);
    }

    /// <summary>
    /// Настройки контекста на Postgres — одни для API и для миграций. Имена в базе —
    /// snake_case (спец. infrastructure, 3.1): C#-модель остаётся PascalCase, а таблицы,
    /// колонки и ограничения — <c>profiles</c>, <c>user_name</c>, <c>ix_profiles_user_name</c>,
    /// чтобы ручной SQL писался без кавычек
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string? connectionString) =>
        builder
            .UseNpgsql(connectionString, x => x.MigrationsAssembly(typeof(ApplicationContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
}
