namespace PetTemplate.Services.Assistant.Hooks;

/// <summary>
/// Трасса хода (спец. assistant, 13.1): цикл пишет сюда каждый шаг, упавший тоже.
/// Реализация — <see cref="TraceWriter"/>; <see cref="NullRunTrace"/> остаётся для
/// тестов, которым трасса не нужна
/// </summary>
public interface IRunTrace
{
    Task Write(RunStep step, CancellationToken ct);
}
