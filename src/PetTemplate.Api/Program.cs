using PetTemplate.Api.Configs;
using PetTemplate.Api.Middlewares;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Mail;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using PetTemplate.Api.Workers;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var config = builder.Configuration;

services.ConfigureServices(config);
services.AddHttpContextAccessor();

services.AddSignalR();

// Телеметрия (спец. infrastructure, 10): метрики и трассы по OTLP в коллектор, оттуда —
// в Prometheus и Tempo. Адрес — OTEL_EXPORTER_OTLP_ENDPOINT из publish-api.yml; без него
// API не шлёт ничего — так локально и в тестах
var otlp = config["OTEL_EXPORTER_OTLP_ENDPOINT"];

services.AddOpenTelemetry()
    .WithMetrics(b =>
    {
        b.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            // Письма по исходу (спец. mail, 7)
            .AddMeter(MailMetrics.MeterName)
            // Ассистент и справка (спец. assistant, 13.4; help, 10), расход и лимиты (13.5–13.6)
            .AddMeter(AssistantMetrics.MeterName)
            .AddMeter(BillingMetrics.MeterName)
            // Очередь команд: длина, попытки, сколько команда ждала до выполнения
            // и сколько выполнялась — по ней идут письма и индексация поиска в Qdrant
            .AddCommandQueueInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlp))
            b.AddOtlpExporter();
    })
    .WithTracing(b =>
    {
        // Трасса — это запрос целиком и его походы наружу: VK ID и эмбеддер по HTTP,
        // Qdrant по gRPC (он тоже ходит через HttpClient). Свой источник — ход ассистента:
        // прогон корневым спаном, инструменты и обращения к модели — дочерними (спец.
        // assistant, 13.1); обращения к модели пишет обёртка UseOpenTelemetry клиента
        // модели — без текстов, пока выключен TelemetrySensitiveData (13.3).
        // Появится хаб SignalR — его соединение живёт часами и одной трассой ничего
        // не говорит: отфильтровать, как чат в ASU (o.Filter)
        b.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(AssistantMetrics.ActivitySourceName)
            .AddSource("Experimental.Microsoft.Extensions.AI");

        if (!string.IsNullOrWhiteSpace(otlp))
            b.AddOtlpExporter();
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
// Первым: всё ниже — лимитер, логи, обработчик ошибок — видит адрес клиента
// из X-Forwarded-For, а не адрес nginx (ConfigureForwardedHeaders)
app.UseForwardedHeaders();
// Отказ на админском пути — пустой 404, как несуществующий путь (спец. admin, 3.5)
app.UseMiddleware<AdminNotFound>();
app.UseMiddleware<GlobalExceptionHandler>();

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