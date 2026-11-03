namespace PetTemplate.Shared.Errors;

/// <summary>
/// Исключение, за которым стоит <see cref="ErrorMessage"/>: <c>GlobalExceptionHandler</c>
/// кладёт его код и подстановки в тело ответа рядом с текстом.
///
/// Интерфейс, а не общий базовый тип: корень у отказов один (<see cref="Exceptions.DomainException"/>),
/// но код есть не у всех — у <see cref="Exceptions.AdminRejectionException"/> только текст
/// </summary>
public interface ICodedError
{
    string Code { get; }

    IReadOnlyDictionary<string, object>? Params { get; }
}
