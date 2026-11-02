using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Пространство — одна модель на создание, правку и список своих
/// </summary>
public class WorkspaceDto
{
    public Guid Id { get; set; }

    [Required]
    [TrimmedStringLength(WorkspaceConstants.NameMaxLength, MinimumLength = 1)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Сколько людей в пространстве — только в списке своих пространств. Подтверждение
    /// выхода по нему узнаёт последнего: пространство без участников удаляется
    /// (спец. authorization, 7.2 и 8), и об этом надо сказать до выхода.
    /// На создании и правке не читается
    /// </summary>
    public int? MembersCount { get; set; }
}
