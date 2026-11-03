namespace PetTemplate.Services.Assistant.Hooks;

/// <summary>Трассы нет: шаг никуда не пишется. Для тестов, которым трасса не нужна</summary>
public sealed class NullRunTrace : IRunTrace
{
    public Task Write(RunStep step, CancellationToken ct) => Task.CompletedTask;
}
