namespace PetTemplate.Shared.Enums;

/// <summary>
/// Состояние запуска из админки (спец. admin, 15.3 и 16.3). Номера уезжают в базу —
/// добавлять только в конец; <see cref="Running"/> — ноль, на нём держится частичный
/// уникальный индекс «один запуск одного вида за раз».
///
/// Продублировано в <c>web/projects/admin/src/app/core/admin-job.ts</c>
/// </summary>
public enum AdminJobStatus
{
    Running = 0,

    /// <summary>Всё поставленное отработано — оценено, пропущено или сорвалось</summary>
    Done = 1,

    /// <summary>Запуск не смог продолжаться: модель пропала, процесс бросил его</summary>
    Failed = 2
}
