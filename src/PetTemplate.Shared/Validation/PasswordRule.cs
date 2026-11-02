using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Validation;

/// <summary>
/// Требования к новому паролю — одни на регистрацию, регистрацию по приглашению, «задать»,
/// «сменить» и сброс (спец. accounts, 8.1). Пароль не обрезается: пробел по краям —
/// тоже символ, который человек ввёл.
///
/// Нижняя граница — 8. На вход она не влияет: <c>LoginDto</c> пускает и прежние короткие
/// пароли (<see cref="LegacyMinLength"/>). Верхняя совпадает с <c>LoginDto.Password</c>:
/// пароль, который нельзя ввести на входе, задавать бессмысленно
/// </summary>
public static class PasswordRule
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    /// <summary>
    /// Самый короткий пароль, с которым ещё пускает вход: столько требовала регистрация
    /// шаблона v1 до <see cref="MinLength"/>, и такие пароли в проектах из него есть
    /// </summary>
    public const int LegacyMinLength = 3;
}

/// <summary>
/// Длина пароля по <see cref="PasswordRule"/>. Пустое значение пропускает —
/// обязательность задаёт <c>[Required]</c>. Отказ — тем же кодом, что у длины строки
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class PasswordAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is not string password
        || password.Length == 0
        || password.Length is >= PasswordRule.MinLength and <= PasswordRule.MaxLength;

    public override string FormatErrorMessage(string name) =>
        ValidationError.Pack(ValidationCodes.StringLength,
            ("min", PasswordRule.MinLength), ("max", PasswordRule.MaxLength));
}
