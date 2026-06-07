using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

public class ProfileDto
{
    public Guid Id { get; set; }

    private string? _userName;

    [TrimmedStringLength(20, MinimumLength = 2)]
    public string? UserName
    {
        get => _userName;
        set => _userName = value?.Trim();
    }

    public int? AvatarId { get; set; }
    public string? Comment { get; set; }
    public bool IsAdult { get; set; }
    public bool HasAcceptedTerms { get; set; }
    public string? WalletAddress { get; set; }
}