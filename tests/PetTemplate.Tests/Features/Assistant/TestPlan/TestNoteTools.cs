using System.ComponentModel;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Assistant.Tools;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Tests.Features.Assistant.TestPlan;

/// <summary>
/// Инструменты тестового раздела (спец. assistant, 7.1): так их приносит раздел проекта —
/// читающий объявляет право раздела (в тестах его изображает
/// <see cref="PermissionEnum.WorkspaceInfoCrud"/>), пишущие кладут операцию в план
/// и ничего не меняют сами: их права проверит операция при применении
/// </summary>
public sealed class TestNoteTools(PlanCollector collector, TestNoteStore store) : IAssistantToolSource
{
    public const string Rules = "Заметки теста: сначала find_test_notes, потом правка.";

    public string Instructions => Rules;

    public IEnumerable<AssistantTool> Tools() =>
    [
        AssistantTool.Create(FindNotes, "find_test_notes", PermissionEnum.WorkspaceInfoCrud),
        AssistantTool.Create(CreateNote, "create_test_note"),
        AssistantTool.Create(CreateBook, "create_test_book"),
        AssistantTool.Create(RenameNote, "rename_test_note")
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
