using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Exceptions;

/// <summary>
/// Отказ по сути запроса, для которого нужна база или состояние, — ответ 400 с кодом
/// для перевода. То, что видно из самого запроса (пустой набор прав), — не сюда,
/// а в проверку модели: <c>422</c> у поля (спец. authorization, 5).
/// </summary>
public class BadRequestException(ErrorMessage message) : DomainException(message.Text), ICodedError
{
    public string Code { get; } = message.Code;

    public IReadOnlyDictionary<string, object>? Params { get; } = message.Params;
}
