using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Логин и контакты на входе (<c>RegisterDto</c>). Правила одни с профилем и приглашением
/// (спец. accounts, 4.1 и 8.1); длину логина и почты держат они — у <c>citext</c> в базе
/// предела нет (спец. infrastructure, 3.1)
/// </summary>
public class UserBaseDto : IValidatableObject
{
    /// <summary>Правила — <see cref="UserNameRule"/> (спец. accounts, 4.1)</summary>
    [Required]
    [UserName]
    public string UserName { get; set; } = null!;

    /// <summary>Телефон — он же логин, вид — <see cref="PhoneRule"/>. Необязателен</summary>
    [PhoneNumber]
    public string? Phone { get; set; }

    /// <summary>Почта — необязательна; предел — тот же, что у почты в профиле (8.4)</summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// Формат почты — здесь, а не <c>[EmailAddress]</c>: пустая строка законна («почты нет»),
    /// а атрибут её отбил бы
    /// </summary>
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Email)) yield break;

        if (!new EmailAddressAttribute().IsValid(Email.Trim()))
        {
            yield return new ValidationResult(ValidationCodes.Email, [nameof(Email)]);
        }
    }
}
