namespace PetTemplate.Api.Workers;

/// <summary>Имена дорожек очереди команд (<see cref="CommandQueueLane"/>)</summary>
public static class CommandQueueLanes
{
    /// <summary>Письма, индексация поиска и всё, что не заявило своей дорожки</summary>
    public const string Default = "default";

    /// <summary>
    /// Ходы и компакция ассистента (спец. assistant, раздел 3): минуты на команду,
    /// <c>AssistantOptions.Workers</c> циклов
    /// </summary>
    public const string Assistant = "assistant";

    /// <summary>
    /// Фоновая работа админки (спец. admin, 15.3 и 16.3): сверка модели набором фраз —
    /// два десятка ходов на одну команду — и пачка автооценок. Один цикл: запуск каждого
    /// вида и так один за раз, а ходы людей (<see cref="Assistant"/>) и письма
    /// (<see cref="Default"/>) за ней не стоят. Оценка, нажатая во время сверки, ждёт её конца
    /// </summary>
    public const string Admin = "admin";
}
