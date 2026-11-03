namespace PetTemplate.Services.Assistant;

/// <summary>Чем ход кончился по воле инструментов (<see cref="RunOutcome"/>)</summary>
public enum RunOutcomeKind
{
    /// <summary>Ход кончился обычным ответом</summary>
    None,

    /// <summary>Задан уточняющий вопрос — ход завершён (5.2)</summary>
    AskUser,

    /// <summary>План применён: в ленту едет отчёт (5.1)</summary>
    PlanApplied
}
