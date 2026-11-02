using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Регистрация по приглашению (спец. authorization, 6.3, сценарий Б).
/// Спрашиваем только логин и пароль: занятость почты или телефона — единственный
/// способ, которым анонимный эндпоинт мог бы подтвердить существование чужого
/// аккаунта по чужому адресу. Занятость логина неизбежна в любой системе
/// с уникальными никами и ничего нового не открывает
/// </summary>
public class InvitationRegisterDto
{
    public Guid Token { get; set; }

    /// <summary>Правила — <see cref="UserNameRule"/> (спец. accounts, 4.1), те же, что у регистрации</summary>
    [Required]
    [UserName]
    public string UserName { get; set; } = null!;

    /// <summary>Требования — <see cref="PasswordRule"/> (8.1)</summary>
    [Required]
    [Password]
    [DataType(DataType.Password)]
    public string Password { get; set; } = null!;

    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    public string PasswordConfirm { get; set; } = null!;

    /// <summary>
    /// Язык интерфейса — как у обычной регистрации (спец. accounts, 8.4): не проверяется,
    /// незнакомый ложится языком по умолчанию
    /// </summary>
    [MaxLength(LanguageConstants.CodeMaxLength)]
    public string? Language { get; set; }
}
