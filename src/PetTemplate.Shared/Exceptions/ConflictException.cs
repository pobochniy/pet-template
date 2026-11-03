using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Exceptions;

/// <summary>
/// Состояние изменилось с тех пор, как человек его видел, — ответ 409. Первый
/// пользователь — слияние аккаунтов (спец. accounts, 7.4): состав пространств
/// поменялся между просмотром и подтверждением, человек подтверждал не то, что случится
/// </summary>
public class ConflictException(ErrorMessage message) : DomainException(message.Text), ICodedError
{
    public string Code { get; } = message.Code;

    public IReadOnlyDictionary<string, object>? Params { get; } = message.Params;
}
