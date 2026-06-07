using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto;

public class AuthenticationResultDto
{
    public string Token { get; set; }
    public ProfileDto Profile { get; set; }
    public IEnumerable<PermissionEnum> Permissions { get; set; }
}