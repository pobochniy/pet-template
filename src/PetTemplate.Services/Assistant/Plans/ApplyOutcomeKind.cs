namespace PetTemplate.Services.Assistant.Plans;

/// <summary>Исход применения плана (<see cref="ApplyOutcome"/>)</summary>
public enum ApplyOutcomeKind
{
    Applied,

    AlreadyApplied,

    Cancelled,

    /// <summary>
    /// План разъехался с базой — не применено <b>ничего</b> (5.4)
    /// </summary>
    Mismatch,

    NotFound
}
