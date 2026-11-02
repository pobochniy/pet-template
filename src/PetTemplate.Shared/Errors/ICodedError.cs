namespace PetTemplate.Shared.Errors;

/// <summary>
/// Исключение, за которым стоит <see cref="ErrorMessage"/>: <c>GlobalExceptionHandler</c>
/// кладёт его код и подстановки в тело ответа рядом с текстом.
///
/// Интерфейс, а не общий базовый тип, потому что статус ответа выбирается
/// по типу исключения (<see cref="Exceptions.NotFoundException"/> — 404,
/// <see cref="Exceptions.BadRequestException"/> — 400), и наследование у них разное
/// </summary>
public interface ICodedError
{
    string Code { get; }

    IReadOnlyDictionary<string, object>? Params { get; }
}
