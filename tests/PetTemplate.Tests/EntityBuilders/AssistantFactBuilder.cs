using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Tests.EntityBuilders;

/// <summary>
/// Факт памяти ассистента (спец. assistant, 12.5): общий для пространства, пока не назван
/// владелец (<see cref="Of"/>)
/// </summary>
public class AssistantFactBuilder
{
    private readonly AssistantFact _fact;

    public AssistantFactBuilder(Guid workspaceId, string key, string text)
    {
        _fact = new AssistantFact
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            Kind = AssistantFactKind.Alias,
            Key = key,
            Text = text,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>Личный факт человека в этом пространстве</summary>
    public AssistantFactBuilder Of(Guid userId)
    {
        _fact.UserId = userId;
        return this;
    }

    public AssistantFactBuilder Kind(AssistantFactKind kind)
    {
        _fact.Kind = kind;
        return this;
    }

    public AssistantFactBuilder Used(int count)
    {
        _fact.UseCount = count;
        return this;
    }

    public AssistantFactBuilder FromRun(Guid runId)
    {
        _fact.SourceRunId = runId;
        return this;
    }

    public AssistantFact Please() => _fact;
}
