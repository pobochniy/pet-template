using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PetTemplate.Api.Workers;

namespace PetTemplate.Tests;

/// <summary>
/// Factory для тестов на настоящем PostgreSQL (<see cref="Containers.PostgresServer"/>).
/// Базу приложению подставляет <see cref="SetupApplicationExt.SetupApplication{TStartup}"/>
/// строкой подключения — регистрация контекста остаётся продовой (Npgsql, snake_case).
///
/// <paramref name="withQueueWorker"/> — разбирать ли очередь команд. API-тесту воркер
/// мешает: он отправил бы поставленное письмо и переписал токен раньше, чем тест проверит,
/// что команда легла в очередь (<c>QueueAssert</c>). Обработку проверяют тесты воркера
/// (<c>Features/Account/MailWorker.cs</c>)
/// </summary>
public class ApiApplicationFactory<TStartup>(bool withQueueWorker = false) : WebApplicationFactory<TStartup>
    where TStartup : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Явно, а не по умолчанию WebApplicationFactory: настройки — дев-машины
        // (appsettings.Development.json), а не сервера. Схема у тестов уже есть — база
        // каждого теста скопирована из шаблонной, которую один раз мигрировал PostgresServer
        builder.UseEnvironment("Development");

        // Очередь без разбора: команды ложатся в local_queue и ждут проверки теста,
        // обработчики остаются в контейнере (CommandQueueOptions.Enabled)
        if (!withQueueWorker)
        {
            builder.UseSetting("CommandQueueOptions:Enabled", "false");
        }

        builder.ConfigureServices(services =>
        {
            // Суточная чистка делает первый проход сразу после старта — она удаляла бы
            // подготовленные тестом старые приглашения и пространства до его проверок.
            // Тесты чистки зовут сервис сами
            var cleanup = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DailyCleanupWorker));

            if (cleanup != null) services.Remove(cleanup);

            // Qdrant обязателен в бою (спец. infrastructure, 6), а приложению под тестом —
            // нет: инициализатор без контейнера уронил бы старт каждого теста. Тесты поиска
            // заводят коллекцию сами той же миграцией (Containers/QdrantServer)
            var qdrant = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(QdrantInitializer));

            if (qdrant != null) services.Remove(qdrant);
        });
    }
}
