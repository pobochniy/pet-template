using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Validation;

/// <summary>
/// Язык интерфейса — код из белого списка <see cref="LanguageConstants.Supported"/>
/// либо не задан вовсе. Пустое значение законно: <c>Account/Save</c> без языка язык
/// не меняет. Обязательность, где она нужна, задаёт <c>[Required]</c>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class SupportedLanguageAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is null || (value is string code && (code.Length == 0 || LanguageConstants.IsSupported(code)));

    public override string FormatErrorMessage(string name) =>
        ValidationError.Pack(ValidationCodes.Language, ("supported", string.Join(", ", LanguageConstants.Supported)));
}
