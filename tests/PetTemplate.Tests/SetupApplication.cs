using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Admin;

namespace PetTemplate.Tests;

public static class SetupApplicationExt
{
    /// <param name="dbName">
    /// Логическое имя базы теста: приложения одного теста с одинаковым именем живут на одной
    /// базе, с разными — на разных. Сама база — копия шаблона на общем сервере
    /// (<see cref="Containers.PostgresServer"/>), после теста её дропает <see cref="TestHosts"/>
    /// </param>
    /// <param name="workspaceId">
    /// Активное пространство клиента — заголовок <c>X-Workspace-Id</c> на всех его запросах,
    /// как его ставит интерцептор на клиенте (спец. authorization, 1.2)
    /// </param>
    public static async Task<(HttpClient client, IServiceProvider services)> SetupApplication<TStartup>(
        this WebApplicationFactory<TStartup> factory,
        Action<ApplicationContext>? dbArrange = null,
        string dbName = "",
        Action<IDictionary<string, string?>>? configureSettings = null,
        string? bearerToken = null,
        Action<IServiceCollection>? configureServices = null,
        Guid? workspaceId = null) where TStartup : class
    {
        // Приложение отдаётся владельцу первым делом: остановить его надо
        // в конце теста, в том числе если тест сорвётся прямо тут, на настройке
        var owner = TestHosts.Current;
        owner.Own(factory);

        var connectionString = await owner.DatabaseAsync(dbName);

        IServiceProvider? rootServices = null;
        var client = factory.WithWebHostBuilder(builder =>
        {
            // Настройка перебивается всегда: то, что осталось бы от appsettings.json,
            // ведёт в дев-базу из docker-compose.yml, а на сервере сборки — неизвестно куда
            builder.UseSetting("ConnectionStrings:AppConnection", connectionString);

            // Лог хоста в прогоне — только ошибки: каждая команда EF уровнем Information
            // и предупреждения отрицательных сценариев топили бы упавший тест в тысячах
            // строк. EF молчит совсем — его «Failed executing DbCommand» пишется и там,
            // где тест нарочно упирается в уникальный индекс и код это ловит; настоящая
            // ошибка базы и так дойдёт до теста исключением или до лога 500-го ответа
            // (`GlobalExceptionHandler`). Тест, которому нужен лог, перебьёт уровень своим configureSettings
            builder.UseSetting("Logging:LogLevel:Default", "Error");
            builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Error");
            builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "None");
            // Опубликованной справки у прогона нет (спец. help, 6.4): опрос пишет
            // «справка недоступна» ошибкой на каждом хосте. Индекс тесты строят сами (Features/Help)
            builder.UseSetting("Logging:LogLevel:PetTemplate.Services.Help.HelpSearch", "None");

            // Эмбеддера в тестах нет: регистрируется NullEmbedder, и индексация тихо
            // не делается. Тесты поиска перебивают адрес своим configureSettings
            // и подставляют фейковый эмбеддер (Features/Search)
            builder.UseSetting("SearchOptions:EmbeddingsEndpoint", "");

            if (configureSettings != null)
            {
                var settings = new Dictionary<string, string?>();
                configureSettings(settings);
                foreach (var setting in settings)
                {
                    builder.UseSetting(setting.Key, setting.Value);
                }
            }

            builder.ConfigureServices(services =>
            {
                configureServices?.Invoke(services);

                var sp = services.BuildServiceProvider();

                using var scope = sp.CreateScope();
                var scopedServices = scope.ServiceProvider;
                var db = scopedServices.GetRequiredService<ApplicationContext>();
                if (dbArrange != null) dbArrange(db);
                db.SaveChanges();
                rootServices = sp;
            });
        // Куки клиент не хранит: у шлюза админки (спец. admin, 3.3) кука Secure и на
        // .example.com — на http://localhost хранилище клиента её либо молча не пошлёт,
        // либо уронит разбор. Тест, которому нужна кука, шлёт её сам
        }).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Провайдер, собранный выше поверх тех же регистраций, — это второй
        // DI-контейнер на тест со своим соединением с базой. Тест держит на нём
        // ApplicationContext, так что закрывается он вместе с приложением
        if (rootServices is IAsyncDisposable provider) owner.Own(provider);

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (workspaceId != null)
        {
            client.DefaultRequestHeaders.Add(WorkspaceConstants.Header, workspaceId.ToString());
        }

        return (client, rootServices!);
    }

    /// <summary>
    /// Ходить в админку с токеном из ответа <c>Admin/Login</c> — токеном своей схемы
    /// (спец. admin, 3.3): продукт его не примет, как и админка — токен продукта
    /// </summary>
    public static void UseAdminSession(this HttpClient client, AdminLoginResultDto session)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
    }
}
