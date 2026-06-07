using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Db.Entity;

namespace PetTemplate.Tests;

public static class SetupApplicationExt
{
    public static async Task<(HttpClient client, IServiceProvider services)> SetupApplication<TStartup>(
        this WebApplicationFactory<TStartup> factory,
        Action<ApplicationContext>? dbArrange = null,
        string dbName = "",
        Action<IDictionary<string, string?>>? configureSettings = null,
        string? bearerToken = null,
        Action<IServiceCollection>? configureServices = null) where TStartup : class
    {
        IServiceProvider? rootServices = null;
        var client = factory.WithWebHostBuilder(builder =>
        {
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
                services.AddDbContext<ApplicationContext>(options =>
                    options
                        .UseInMemoryDatabase("Testing_" + dbName)
                        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                );

                configureServices?.Invoke(services);

                var sp = services.BuildServiceProvider();

                using var scope = sp.CreateScope();
                var scopedServices = scope.ServiceProvider;
                var db = scopedServices.GetRequiredService<ApplicationContext>();
                db.Database.EnsureCreated();

                if (dbArrange != null) dbArrange(db);
                db.SaveChanges();
                rootServices = sp;
            });
        }).CreateClient();

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return (client, rootServices!);
    }
}