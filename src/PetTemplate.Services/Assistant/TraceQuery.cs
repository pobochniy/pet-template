using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Шаги трассы одного прогона по порядку — один запрос на двоих: «показать ход мысли»
/// у своего прогона (спец. assistant, 13.2) и разбор диалогов в админке (спец. admin, 16.2),
/// где он же идёт со снятым фильтром по пользователю. Кто смотрит — решает вызывающий;
/// здесь только какие шаги и в каком порядке
/// </summary>
public static class TraceQuery
{
    public static IQueryable<AssistantTraceStep> OfRun(this IQueryable<AssistantTraceStep> steps, Guid runId) =>
        steps
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.Step)
            .ThenBy(x => x.Id);
}
