namespace PetTemplate.Shared.Enums;

/// <summary>
/// Что запустили из админки (спец. admin, 15.3 и 16.3). Номера — те же, что у
/// <see cref="ServiceSpendKind"/>: расходы запуска пишутся тем же видом. Уезжают
/// в базу — добавлять только в конец.
///
/// Продублировано в <c>web/projects/admin/src/app/core/admin-job.ts</c>
/// </summary>
public enum AdminJobKind
{
    None = 0,

    /// <summary>Сверка модели набором фраз (спец. admin, 15.3)</summary>
    ModelCheck = 2,

    /// <summary>Автооценка пачки диалогов (спец. admin, 16.3)</summary>
    DialogEvaluation = 3
}
