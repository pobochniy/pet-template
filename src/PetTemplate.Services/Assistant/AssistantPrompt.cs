using Microsoft.Extensions.AI;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Промпт хода двумя частями (спец. assistant, раздел 9): наши инструкции —
/// системным сообщением, данные людей — отдельным сообщением с блоком
/// <c>&lt;app_data&gt;</c> перед перепиской
/// </summary>
/// <param name="System">Системный промпт: шаблон, время, язык, правила разделов</param>
/// <param name="Data">Блок данных: пространство, участники, факты, резюме, операции плана</param>
public sealed record AssistantPrompt(string System, string Data)
{
    /// <summary>
    /// Начало запроса к модели. Блок данных — ролью <c>user</c>: роль <c>tool</c> требует
    /// вызова, которого не было, а системной ролью данные стали бы инструкциями.
    /// Что это не реплика человека, говорят системный промпт и сам тег
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages() =>
    [
        new(ChatRole.System, System),
        new(ChatRole.User, Data)
    ];
}
