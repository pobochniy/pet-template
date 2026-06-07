using System.ComponentModel.DataAnnotations;

namespace PetTemplate.Shared.Dto;

public record SetAvatarDto
{
    [Required]
    [Range(0, 9999)]
    public int AvatarId { get; set; }
}
