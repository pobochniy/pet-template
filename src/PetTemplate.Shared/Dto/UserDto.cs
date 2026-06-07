using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto;

public class UserDto
{
    public Guid Id { get; set; }

    public ProfileDto Profile { get; set; }

    public IEnumerable<PermissionEnum> Permissions { get; set; }
}