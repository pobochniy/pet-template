using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Options;
using Qdrant.Client;

namespace PetTemplate.Api.Configs;

/// <summary>
/// <c>GET /health</c> (спец. infrastructure, 6): база и Qdrant достижимы — 200 <c>Healthy</c>,
/// иначе 503. Без них API не работает (Qdrant обязателен, как база), а эмбеддер и модель
/// ассистента — нет, поэтому их здесь нет: лежащий эмбеддер не должен откатывать выкатку.
///
/// Анонимно и вне <c>/api/</c>: nginx проксирует наружу только <c>/api/</c>, так что
/// с интернета адрес не виден; спрашивают его выкатка (<c>IaC/run-api.sh</c> по
/// <c>127.0.0.1:5000</c>) и <c>HEALTHCHECK</c> образа
/// </summary>
public static class HealthChecks
{
    public const string Path = "/health";

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        // Синглтон: QdrantClient держит gRPC-канал, на каждую проверку его не заводим
        services.AddSingleton<QdrantHealthCheck>();
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", timeout: TimeSpan.FromSeconds(5))
            .AddCheck<QdrantHealthCheck>("qdrant", timeout: TimeSpan.FromSeconds(5));

        return services;
    }

    public static void MapApiHealthChecks(this IEndpointRouteBuilder app) =>
        app.MapHealthChecks(Path).AllowAnonymous();
}

internal sealed class PostgresHealthCheck(ApplicationContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("PostgreSQL недоступен");
}

internal sealed class QdrantHealthCheck(IOptions<QdrantOptions> options) : IHealthCheck, IDisposable
{
    private readonly QdrantClient _client = new(host: options.Value.Host, port: options.Value.Port);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await _client.HealthAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Qdrant недоступен", e);
        }
    }

    public void Dispose() => _client.Dispose();
}
