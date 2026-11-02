using PetTemplate.Shared.Dto.Assistant;

namespace PetTemplate.Services.Assistant.Plans;

/// <summary>
/// Чем кончилось применение плана (спец. assistant, 5.1)
/// </summary>
public class ApplyOutcome
{
    public ApplyOutcomeKind Kind { get; set; }

    /// <summary>
    /// Что получилось по операциям. Есть у <see cref="ApplyOutcomeKind.Applied"/>
    /// и у <see cref="ApplyOutcomeKind.AlreadyApplied"/> — второе «да» отвечает
    /// ссылками на уже сделанное (5.1)
    /// </summary>
    public PlanResult? Result { get; set; }

    /// <summary>
    /// Расхождения для модели: дальше обычный круг 5.1 — ассистент пересобирает план,
    /// пишет, что изменилось, и спрашивает снова
    /// </summary>
    public List<string> Mismatches { get; set; } = [];
}
