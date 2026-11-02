using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Assistant;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Факт наружу — только так: ключ и счётчики использования остаются на сервере,
/// человеку они ничего не говорят (12.5)
/// </summary>
public static class MemoryMapping
{
    public static AssistantFactDto ToDto(AssistantFact fact) => new()
    {
        Id = fact.Id,
        Kind = fact.Kind,
        Text = fact.Text,
        Personal = fact.UserId != null,
        CreatedAtUtc = fact.CreatedAtUtc,
        SourceRunId = fact.SourceRunId
    };
}
