namespace PetTemplate.Services.Assistant.Hooks;

/// <summary>
/// Шаг хода для трассы (спец. assistant, 13.1): обращение к модели
/// (<see cref="Kind"/> = <c>model</c>) или вызов инструмента (<c>tool</c>).
/// Номер шага — номер вызова инструмента, им же нумерована плашка в ленте (раздел 3)
/// </summary>
public record RunStep(
    Guid RunId,
    int Step,
    string Kind,
    string? ToolName,
    string Input,
    string? Output,
    long TokensIn,
    long TokensOut,
    int DurationMs,
    string? Error)
{
    public const string Model = "model";

    public const string Tool = "tool";
}
