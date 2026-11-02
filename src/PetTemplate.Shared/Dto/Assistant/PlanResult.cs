namespace PetTemplate.Shared.Dto.Assistant;

/// <summary>
/// Содержимое колонки <c>assistant_plans.result</c> (спец. assistant, 12.2.1) —
/// что из плана получилось.
///
/// Пишется <b>после каждой операции</b>, а не пакетом в конце: процесс умер после
/// третьей операции из пяти — очередь повторит команду, и исполнитель пропустит те три,
/// у которых результат уже есть, вместо того чтобы сделать их второй раз (5.1).
/// Отсюда же правило чтения: <b>нет записи по индексу — до операции дело не дошло</b>,
/// и в отчёте она стоит без значка.
/// </summary>
public class PlanResult
{
    public List<PlanOperationResult> Operations { get; set; } = [];
}

/// <summary>
/// Результат одной операции плана
/// </summary>
public class PlanOperationResult
{
    /// <summary>
    /// Номер операции из <see cref="PlanOperation.Index"/>
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Операция дошла до конца. <c>false</c> вместе с <see cref="Error"/> — отказ:
    /// в отчёте крестик с причиной одной строкой (5.1)
    /// </summary>
    public bool Done { get; set; }

    /// <summary>
    /// Идентификатор созданного — строкой: у сущностей разделов он может быть и
    /// <c>long</c>, и <c>Guid</c>. Он же подставляется вместо ссылок <c>"#n"</c>
    /// в следующих операциях (5.1)
    /// </summary>
    public string? CreatedId { get; set; }

    /// <summary>
    /// Причина отказа ровно в том виде, в каком её вернул сервис
    /// </summary>
    public string? Error { get; set; }
}
