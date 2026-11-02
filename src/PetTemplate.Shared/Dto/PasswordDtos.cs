using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Тело <c>Account/SetPassword</c> (спец. accounts, 8.1): аккаунту без пароля — новый дважды
/// </summary>
public class SetPasswordDto
{
    [Required]
    [Password]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = null!;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword))]
    public string NewPasswordConfirm { get; set; } = null!;
}

/// <summary>Тело <c>Account/ChangePassword</c> (8.1): текущий и новый дважды</summary>
public class ChangePasswordDto
{
    [Required]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = null!;

    [Required]
    [Password]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = null!;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword))]
    public string NewPasswordConfirm { get; set; } = null!;
}

/// <summary>«Забыли пароль?» — подтверждённая почта аккаунта (спец. accounts, 8.4)</summary>
public class RequestResetDto
{
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = null!;
}

/// <summary>Новый пароль по ссылке сброса — <c>/auth/reset/&lt;токен&gt;</c> (8.2)</summary>
public class ResetPasswordDto
{
    [Required]
    public string Token { get; set; } = null!;

    [Required]
    [Password]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = null!;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword))]
    public string NewPasswordConfirm { get; set; } = null!;
}

/// <summary>Токен из ссылки письма — <c>Auth/ConfirmEmail</c> (8.4)</summary>
public class EmailTokenDto
{
    [Required]
    public string Token { get; set; } = null!;
}

/// <summary>Язык из переключателя в шапке — <c>Account/SetLanguage</c> (8.4)</summary>
public class SetLanguageDto
{
    [Required]
    [SupportedLanguage]
    public string Language { get; set; } = null!;
}

/// <summary>
/// Подтвердить владение паролем (спец. accounts, 8.6) — перед привязкой, отвязкой, сменой
/// почты и слиянием
/// </summary>
public class ConfirmPasswordDto
{
    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = null!;
}
