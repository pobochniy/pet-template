using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Services.Assistant.Check;
using PetTemplate.Services.Assistant.Sections;

namespace PetTemplate.Tests.Features.Assistant.TestPlan;

/// <summary>
/// Тестовый раздел целиком (спец. assistant, 7.1): так раздел проекта подключается к
/// ассистенту одной регистрацией — инструменты, операции, хранилище, глаголы трассы.
/// Хранилище — своё у мира теста, поэтому раздел приходит экземпляром
/// </summary>
public sealed class TestNoteSection(TestNoteStore store) : IAssistantSection
{
    public const string FindVerb = "искал тестовые заметки";

    /// <summary>Фраза сверки раздела — идёт в набор после фраз ядра (спец. admin, 15.3)</summary>
    public const string CheckPhrase = "заведи заметку «Купить хлеб» и сразу сохрани";

    public IReadOnlyDictionary<string, string> Verbs => new Dictionary<string, string>
    {
        ["find_test_notes"] = FindVerb
    };

    public IReadOnlyList<ModelCheckPhrase> Phrases => [new() { Phrase = CheckPhrase, Tool = "apply_plan" }];

    public void Register(AssistantSectionBuilder section)
    {
        section.Services.AddSingleton(store);
        section
            .Tools<TestNoteTools>()
            .Operation<CreateTestNoteOperation>()
            .Operation<CreateTestBookOperation>()
            .Operation<RenameTestNoteOperation>();
    }
}
