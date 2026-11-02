using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Validation;

/// <summary>
/// Телефон — он же логин (спец. accounts, раздел 3): <c>+</c> и 11 цифр. По этому же
/// виду форма входа отличает телефон от логина и почты (<c>LoginDto.IsPhone</c>), поэтому
/// телефон другого вида сохранить можно было бы, а войти им — нет. Одно место на вход,
/// регистрацию и профиль
/// </summary>
public static partial class PhoneRule
{
    [GeneratedRegex(@"^\+[0-9]{11}$")]
    private static partial Regex Format();

    public static bool IsValid(string? phone) => phone != null && Format().IsMatch(phone);
}

/// <summary>
/// Телефон по <see cref="PhoneRule"/>. Пустое значение пропускает: телефон необязателен,
/// а в профиле пустая строка — «убрать». Наружу уезжает код, а не фраза
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class PhoneNumberAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string phone || string.IsNullOrWhiteSpace(phone)) return ValidationResult.Success;

        var memberNames = validationContext.MemberName is null ? null : new[] { validationContext.MemberName };
        return PhoneRule.IsValid(phone.Trim())
            ? ValidationResult.Success
            : new ValidationResult(ValidationCodes.PhoneFormat, memberNames);
    }
}
