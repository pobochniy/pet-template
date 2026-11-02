using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Регистрация по паролю. Правила полей — те же, что у профиля и приглашения
/// (спец. accounts, 4.1 и 8.1): логин, телефон и почта проверяются на <see cref="UserBaseDto"/>,
/// пароль — здесь
/// </summary>
public class RegisterDto : UserBaseDto
{
    [Required]
    [Password]
    [DataType(DataType.Password)]
    public string Password { get; set; } = null!;

    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    public string PasswordConfirm { get; set; } = null!;

    /// <summary>
    /// Название первого пространства, которое заводится тем же сохранением, что
    /// и аккаунт (спец. authorization, 3, п. 1). Пустое — регистрация без пространства:
    /// человек нажал «Меня пригласят — пропустить». Предел — тот же, что у
    /// <see cref="WorkspaceDto.Name"/>
    /// </summary>
    [TrimmedStringLength(WorkspaceConstants.NameMaxLength)]
    public string? WorkspaceName { get; set; }

    /// <summary>
    /// Язык интерфейса на момент регистрации (спец. accounts, 8.4) — на нём уйдут письма.
    /// Не проверяется: его прислал браузер, незнакомый ложится языком по умолчанию
    /// </summary>
    [MaxLength(LanguageConstants.CodeMaxLength)]
    public string? Language { get; set; }
}
