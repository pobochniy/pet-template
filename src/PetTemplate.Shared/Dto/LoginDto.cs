using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

[LoginPassNotTheSame]
public class LoginDto
{
    /// <summary>
    /// Логин, почта или телефон. Предел — по почте (255), а не по логину: с пределом
    /// логина в 20 символов длинная почта не входила бы вовсе
    /// </summary>
    [Required]
    [MinLength(3)]
    [MaxLength(255)]
    public string Login { get; set; } = null!;

    /// <summary>
    /// Нижняя граница — прежняя (<see cref="PasswordRule.LegacyMinLength"/>): новые правила
    /// пароля (спец. accounts, 8.1) на вход не влияют, короткие пароли из базы входят как
    /// входили. Верхняя — <see cref="PasswordRule.MaxLength"/>: прежний предел в 20 не пускал
    /// бы пароли длиннее, заданные по новым правилам
    /// </summary>
    [Required]
    [MinLength(PasswordRule.LegacyMinLength)]
    [MaxLength(PasswordRule.MaxLength)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = null!;

    public bool IsPhone => PhoneRule.IsValid(Login);

    public bool IsEmail => !string.IsNullOrWhiteSpace(Login) && Regex.IsMatch(Login, @".+@.+\..+");
}

internal class LoginPassNotTheSame : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = (LoginDto)validationContext.ObjectInstance;

        return model.Login == model.Password
            ? new ValidationResult(ValidationCodes.LoginEqualsPassword)
            : ValidationResult.Success;
    }
}
