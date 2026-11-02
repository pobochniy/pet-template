using System.Text.Json;
using OpenTelemetry.Metrics;
using PetTemplate.Services.LocalQueueHandlers;

namespace PetTemplate.Api.Workers;

/// <summary>Подключение очереди команд: обработчики и воркер</summary>
public static class CommandQueueRegistration
{
    /// <summary>
    /// Повтор упавшей команды по умолчанию — через минуту, как у письма в Lad
    /// (<c>RetryPolicyOptions.BackoffInterval</c>)
    /// </summary>
    private static readonly TimeSpan DefaultRetryAfterFailure = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Обработчик <typeparamref name="THandler"/> команды <typeparamref name="TCommand"/>.
    /// Команда читается из JSON теми же умолчаниями <c>System.Text.Json</c>, какими её
    /// пишет <c>ToString()</c>
    /// </summary>
    /// <param name="services">Контейнер приложения</param>
    /// <param name="retryAfterFailure">Через сколько повторить упавшую команду</param>
    /// <param name="lane">
    /// Дорожка (<see cref="CommandQueueLane"/>); своя заводится <see cref="AddCommandQueueLane"/>.
    /// По умолчанию — общая, <see cref="CommandQueueLanes.Default"/>
    /// </param>
    public static IServiceCollection AddCommandHandler<TCommand, THandler>(
        this IServiceCollection services,
        TimeSpan? retryAfterFailure = null,
        string lane = CommandQueueLanes.Default)
        where THandler : class, ICommandHandler<TCommand>
    {
        services.AddTransient<THandler>();
        services.AddSingleton(new CommandHandlerRegistration(
            typeof(TCommand).FullName!,
            (provider, data, ct) => provider.GetRequiredService<THandler>()
                .Handle(JsonSerializer.Deserialize<TCommand>(data)!, ct),
            retryAfterFailure ?? DefaultRetryAfterFailure,
            lane));

        return services;
    }

    /// <summary>
    /// Своя дорожка очереди — для команд, которые идут минутами и не должны ни ждать
    /// за чужими, ни держать их (ход ассистента, спец. assistant, раздел 3)
    /// </summary>
    public static IServiceCollection AddCommandQueueLane(this IServiceCollection services, CommandQueueLane lane)
    {
        services.AddSingleton(lane);

        return services;
    }

    public static IServiceCollection AddCommandQueueWorker(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<CommandQueueOptions>().Bind(config.GetSection("CommandQueueOptions"));
        services.AddSingleton<CommandQueueMetrics>();
        // Аренда строки — на область команды: воркер называет строку, обработчик продлевает
        services.AddScoped<CommandLease>();
        services.AddScoped<ICommandLease>(provider => provider.GetRequiredService<CommandLease>());
        services.AddHostedService<CommandQueueWorker>();

        return services;
    }

    /// <summary>
    /// Метрики очереди в OpenTelemetry — замена <c>AddDodoLocalCommandQueueInstrumentation</c>
    /// пакета <c>LocalQueue.OpenTelemetry</c> (Lad, ASU) с теми же границами гистограмм:
    /// без них задержку в минуты гистограмма по умолчанию свалила бы в одну корзину
    /// </summary>
    public static MeterProviderBuilder AddCommandQueueInstrumentation(this MeterProviderBuilder builder) =>
        builder
            .AddMeter(CommandQueueMetrics.MeterName)
            .AddView(CommandQueueMetrics.ProcessingLagName,
                new ExplicitBucketHistogramConfiguration { Boundaries = CommandQueueMetrics.HistogramBoundaries })
            .AddView(CommandQueueMetrics.ProcessingTimeName,
                new ExplicitBucketHistogramConfiguration { Boundaries = CommandQueueMetrics.HistogramBoundaries });
}
