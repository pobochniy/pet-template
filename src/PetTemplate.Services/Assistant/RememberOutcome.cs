using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Чем кончилась запись факта. Вытеснение не молчаливое: ассистент говорит о нём
/// строкой в том же ответе — «запомнил; заодно забыл „отчёт по пятницам“ —
/// им ни разу не пользовались» (спец. assistant, 12.5)
/// </summary>
public class RememberOutcome
{
    /// <summary>Записанный или обновлённый факт; пусто — записывать было нечего</summary>
    public AssistantFact? Fact { get; set; }

    /// <summary>Вытесненный, чтобы освободить место под новый</summary>
    public AssistantFact? Forgotten { get; set; }
}
