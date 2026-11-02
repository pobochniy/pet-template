using System.ComponentModel;
using Microsoft.Extensions.AI;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Assistant.Tools;

namespace PetTemplate.Tests.Features.Assistant.TestPlan;

/// <summary>
/// Инструменты тестового раздела (спец. assistant, 7.1): так их приносит раздел проекта —
/// пишущие кладут операцию в план и ничего не меняют сами
/// </summary>
public sealed class TestNoteTools(PlanCollector collector, TestNoteStore store) : IAssistantToolSource
{
    public const string Rules = "Заметки теста: сначала find_test_notes, потом правка.";

    public string Instructions => Rules;

    public IEnumerable<AITool> Tools() =>
    [
        AIFunctionFactory.Create(FindNotes, "find_test_notes", null, AssistantTools.SchemaJson),
        AIFunctionFactory.Create(CreateNote, "create_test_note", null, AssistantTools.SchemaJson),
        AIFunctionFactory.Create(CreateBook, "create_test_book", null, AssistantTools.SchemaJson),
        AIFunctionFactory.Create(RenameNote, "rename_test_note", null, AssistantTools.SchemaJson)
    ];

    [Description("Заметки: id и название")]
    private string FindNotes() =>
        string.Join("\n", store.Notes.Select(x => $"{x.Key}: {x.Value.Title}"));

    [Description("Записать в план новую заметку")]
    private string CreateNote(string title, string? bookLink = null) =>
        collector.Propose(TestNoteKinds.CreateNote, new CreateTestNoteArgs { Title = title, BookLink = bookLink });

    [Description("Записать в план новую книгу")]
    private string CreateBook(string title) =>
        collector.Propose(TestNoteKinds.CreateBook, new CreateTestBookArgs { Title = title });

    [Description("Записать в план новое название заметки")]
    private string RenameNote(string noteId, string title) =>
        collector.Propose(TestNoteKinds.RenameNote, new RenameTestNoteArgs { NoteId = noteId, Title = title });
}
