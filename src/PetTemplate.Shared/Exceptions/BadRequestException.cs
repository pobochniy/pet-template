using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Exceptions;

/// <summary>
/// Отказ по сути запроса, для которого нужна база или состояние, — ответ 400 с кодом
/// для перевода. То, что видно из самого запроса (пустой набор прав), — не сюда,
/// а в проверку модели: <c>422</c> у поля (спец. authorization, 5).
///
/// Наследник <see cref="ArgumentException"/>, а не новый корень: статус выбирается
/// по типу исключения, и <c>GlobalExceptionHandler</c> уже отвечает на неё 400
/// </summary>
public class BadRequestException(ErrorMessage message) : ArgumentException(message.Text), ICodedError
{
    public string Code { get; } = message.Code;

    public IReadOnlyDictionary<string, object>? Params { get; } = message.Params;
}
