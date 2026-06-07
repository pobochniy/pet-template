using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PetTemplate.Db.Entity;

namespace PetTemplate.Tests;

/// <summary>
/// Factory для тестов с InMemory БД
/// </summary>
public class ApiApplicationFactory<TStartup> : WebApplicationFactory<TStartup>
    where TStartup : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove DbContext registration
            services.RemoveAll(typeof(DbContextOptions<ApplicationContext>));
            services.RemoveAll(typeof(ApplicationContext));
            
            // Remove all EF Core database provider services to avoid conflicts
            var descriptors = services
                .Where(d => d.ServiceType.Namespace?.Contains("EntityFrameworkCore") == true)
                .ToList();
            
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }
        });
    }
}
