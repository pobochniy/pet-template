namespace PetTemplate.Shared.Dto;

/// <summary>
/// Человек в списках — участники пространства, выбор участника в разделах проекта.
/// Сущность <c>Profile</c> наружу не едет: в ней почта, телефон и основное пространство,
/// а контакты — только в своём профиле (спец. authorization, 5;
/// <c>tech-debt/profile-contacts.md</c>)
/// </summary>
public class ProfileSummaryDto
{
    public Guid Id { get; set; }

    public string? UserName { get; set; }

    public int? AvatarId { get; set; }
}
