using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Exceptions;

/// <summary>
/// Запрошенной сущности нет либо она не видна пользователю — ответ 404.
/// Отдельный тип, а не <see cref="KeyNotFoundException"/>: тот рождается у любого
/// промаха <c>Dictionary[key]</c>, и ловить его глобально значит выдавать 404
/// вместо 500 на настоящий баг
/// </summary>
public class NotFoundException(ErrorMessage message) : Exception(message.Text), ICodedError
{
    public string Code { get; } = message.Code;

    public IReadOnlyDictionary<string, object>? Params { get; } = message.Params;
}
