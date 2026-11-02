using LocalQueue.Processing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PetTemplate.Api.Workers;

namespace PetTemplate.Tests.Features.Queue;

/// <summary>
/// Обвязка очереди без базы (спец. mail, 4; спец. infrastructure, 2.2): пауза повтора,
/// выключенный разбор и регистрация рядом с keyed-службами
/// </summary>
public class QueueRegistrationTests
{
    /// <summary>
    /// Пауза удваивается с каждой попыткой и упирается в потолок: команда, которая
    /// не пройдёт никогда, заходит раз в час, а не раз в минуту
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(1000, 60)]
    public void BackoffDoublesUpToCeiling(int attempt, int expectedMinutes)
    {
        // Arrange
        var policy = new CommandQueueRegistration.BackoffRetryPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

        // Act
        var delay = policy.RedeliverAfter(attempt);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
        Assert.Equal(1, policy.MaxRetryCount);
        Assert.Equal(TimeSpan.Zero, policy.BackoffFor(1));
    }

    /// <summary>Первая пауза дольше потолка — берётся она: потолок не укорачивает заданный повтор</summary>
    [Fact]
    public void FirstDelayAboveCeilingIsKept()
    {
        // Arrange
        var policy = new CommandQueueRegistration.BackoffRetryPolicy(TimeSpan.FromHours(2), TimeSpan.FromHours(1));

        // Act
        var delay = policy.RedeliverAfter(3);

        // Assert
        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    /// <summary>
    /// Разбор выключен: ни одной фоновой службы пакета, а обработчик долгой команды
    /// собирается из контейнера — с арендой, которой нечего продлевать
    /// </summary>
    [Fact]
    public async Task DisabledQueueKeepsHandlersWithoutWorkers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCommandQueueStorage(Config(enabled: false));

        // Act
        services.AddCommandQueueWorker(h => h.Add<LeaseCommand, LeaseHandler>());

        // Assert
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<LeaseHandler>();
        await handler.Handle(new LeaseCommand(), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Keyed-регистрация до очереди не роняет старт: у keyed-дескриптора
    /// <c>ImplementationInstance</c> бросает, и поиск настроек очереди обходит такие
    /// </summary>
    [Fact]
    public void KeyedServicesBeforeQueueDoNotBreakRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedSingleton("key", new object());
        services.AddCommandQueueStorage(Config(enabled: true));

        // Act
        services.AddCommandQueueWorker(h => h.Add<LeaseCommand, LeaseHandler>());

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }

    private static IConfiguration Config(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AppConnection"] = "Host=localhost;Database=pettemplatedb",
                ["CommandQueueOptions:Enabled"] = enabled.ToString()
            })
            .Build();

    public class LeaseCommand;

    public sealed class LeaseHandler(ICommandLease lease) : ICommandHandler<LeaseCommand>
    {
        public Task Handle(LeaseCommand command, CancellationToken ct) => lease.Extend(TimeSpan.FromMinutes(1), ct);
    }
}
