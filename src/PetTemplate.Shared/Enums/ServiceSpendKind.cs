namespace PetTemplate.Shared.Enums;

/// <summary>
/// Вид служебного расхода — обращения к поставщику не ходом человека (спец. assistant, 13.5).
/// Номера уезжают в базу — добавлять только в конец. Номера сверки и автооценки — те же,
/// что у <see cref="AdminJobKind"/>: расход запуска пишется тем же видом.
///
/// Продублировано в <c>web/projects/admin/src/app/core/service-spend-kind.ts</c>
/// </summary>
public enum ServiceSpendKind
{
    /// <summary>Резюме длинного треда после хода (12.4)</summary>
    Compaction = 0,

    /// <summary>
    /// Расшифровка голоса (раздел 6). Единственный служебный вид, который идёт в лимит
    /// пространства: распознавание просил человек, а не система (13.6)
    /// </summary>
    Transcription = 1,

    /// <summary>Сверка модели набором фраз из админки — строка на фразу (спец. admin, 15.3)</summary>
    ModelCheck = 2,

    /// <summary>Автооценка диалога судьёй из админки — строка на прогон (спец. admin, 16.3)</summary>
    DialogEvaluation = 3
}
