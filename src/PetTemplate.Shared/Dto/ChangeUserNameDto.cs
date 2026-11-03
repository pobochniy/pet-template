using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

/// <summary>Тело <c>Account/ChangeUserName</c> (спец. accounts, 4.3)</summary>
public class ChangeUserNameDto
{
    [Required]
    [UserName]
    public string UserName { get; set; } = null!;
}
