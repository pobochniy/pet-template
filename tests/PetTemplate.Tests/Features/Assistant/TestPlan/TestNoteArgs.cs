namespace PetTemplate.Tests.Features.Assistant.TestPlan;

/// <summary>Аргументы «завести заметку»: книга — существующая или ссылка <c>#n</c> на книгу плана</summary>
public sealed class CreateTestNoteArgs
{
    public string Title { get; set; } = string.Empty;

    public string? BookLink { get; set; }
}

/// <summary>Аргументы «завести книгу» — рамку для заметок плана</summary>
public sealed class CreateTestBookArgs
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>Аргументы «переименовать заметку» — правка существующего</summary>
public sealed class RenameTestNoteArgs
{
    public string NoteId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
