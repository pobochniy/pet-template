using PetTemplate.Api.Configs;
using PetTemplate.Api.Middlewares;
using PetTemplate.Db.Entity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var config = builder.Configuration;

services.ConfigureServices(config);
services.AddHttpContextAccessor();

services.AddSignalR();

services.AddOpenTelemetry()
    .WithMetrics(b =>
    {
        b.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.UseMiddleware<GlobalExceptionHandler>();

// app.UseOpenTelemetryPrometheusScrapingEndpoint();
// app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowSpecificOrigin");

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

public partial class Program
{
    /* Expose the Program class for use with WebApplicationFactory<T> */
}