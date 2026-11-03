using PetTemplate.Services.Assistant.Plans;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Чем ход закончился по воле инструментов (спец. assistant, 5.1, 5.2).
///
/// Два инструмента ядра не отвечают модели данными, а решают судьбу хода:
/// <c>ask_user</c> его завершает, <c>apply_plan</c> пишет. Третий, <c>remember_fact</c>,
/// судьбы хода не решает, но оставляет в нём след — строку «Запомнил: …». Цикл про это знать не должен —
/// он остаётся «модель → инструменты → модель», — поэтому сигнал едет scoped-классом,
/// который читает обработчик хода после выхода из цикла.
/// </summary>
public class RunOutcome
{
    public RunOutcomeKind Kind { get; set; }

    /// <summary>
    /// Вопрос человеку. Вопросов на ход ровно один по построению: <c>ask_user</c>
    /// завершает ход (5.2)
    /// </summary>
    public string? Question { get; set; }

    /// <summary>
    /// Варианты ответа — подсказки, а не кнопки действия: нажатие отправляет в ленту
    /// ту самую реплику, которую человек набрал бы руками (5.2)
    /// </summary>
    public List<string> Options { get; set; } = [];

    /// <summary>
    /// Что получилось у применения — из него лента строит отчёт «Готово» (5.1)
    /// </summary>
    public ApplyOutcome? Applied { get; set; }

    /// <summary>
    /// Какой именно план применён: его идентификатор приедет с сообщением,
    /// и по нему клиент нарисует отчёт «Готово» (5.1)
    /// </summary>
    public Guid? AppliedPlanId { get; set; }

    /// <summary>
    /// Факты, записанные этим ходом (<c>remember_fact</c>, 12.5): исхода хода они
    /// не меняют, а едут с сообщением строкой «Запомнил: …»
    /// </summary>
    public List<Guid> FactIds { get; set; } = [];
}
