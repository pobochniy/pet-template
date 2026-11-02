namespace PetTemplate.Api.Workers;

/// <summary>
/// Обработчик одного типа команды: как достать её из JSON, кому отдать и какой
/// дорожкой очереди её разбирать (<see cref="CommandQueueLane"/>)
/// </summary>
public sealed record CommandHandlerRegistration(
    string CommandType,
    Func<IServiceProvider, string, CancellationToken, Task> Handle,
    TimeSpan RetryAfterFailure,
    string Lane);
