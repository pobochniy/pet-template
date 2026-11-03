using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Тело <c>Account/Save</c> (спец. accounts, 6): контакты, комментарий и язык своего профиля.
/// Логина здесь нет — у него свой вызов и свой отказ «занят» (<c>Account/ChangeUserName</c>).
/// Пустое поле — «убрать»: почта и телефон необязательны
/// </summary>
public class AccountSaveDto : IValidatableObject
{
    /// <summary>
    /// До 255 символов (8.4) — предел держит эта проверка: у <c>citext</c> в базе его нет.
    /// Пробелы по краям сервер обрезает, формат проверяет <see cref="Validate"/>:
    /// пустая строка законна, а <c>[EmailAddress]</c> её отбил бы
    /// </summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    [PhoneNumber]
    public string? Phone { get; set; }

    [MaxLength(3000)]
    public string? Comment { get; set; }

    /// <summary>
    /// Язык интерфейса и писем (8.4). Пустое — не меняется: форма «Основное» о языке
    /// не знает, его выбирают в шапке (<c>Account/SetLanguage</c>)
    /// </summary>
    [SupportedLanguage]
    public string? Language { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Email)) yield break;

        if (!new EmailAddressAttribute().IsValid(Email.Trim()))
        {
            yield return new ValidationResult(ValidationCodes.Email, [nameof(Email)]);
        }
    }
}
