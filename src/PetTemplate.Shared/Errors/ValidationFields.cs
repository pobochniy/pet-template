namespace PetTemplate.Shared.Errors;

/// <summary>
/// Имена «полей» ответа 422, за которыми нет поля DTO
/// </summary>
public static class ValidationFields
{
    /// <summary>
    /// Отказ по всему запросу: тело не разобралось, либо правило проверяет форму
    /// целиком (<c>[LoginPassNotTheSame]</c>). Сказать, какое поле виновато, нельзя —
    /// иногда и не нужно
    /// </summary>
    public const string Payload = "payload";
}
