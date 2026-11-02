using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Tests.Features.Assistant.TestPlan;

/// <summary>Виды операций тестового раздела</summary>
public static class TestNoteKinds
{
    public const string CreateNote = "CreateTestNote";

    public const string CreateBook = "CreateTestBook";

    public const string RenameNote = "RenameTestNote";

    /// <summary>Название, на котором сервис раздела отказывает — отказ одной операции</summary>
    public const string Refused = "сломай";
}

/// <summary>
/// Завести заметку — создание, применимое сразу; ссылается на книгу этого же плана.
/// Заметка с таким же названием уже есть — расхождение (5.4)
/// </summary>
public sealed class CreateTestNoteOperation(TestNoteStore store) : PlanOperationHandler<CreateTestNoteArgs>
{
    public override string Kind => TestNoteKinds.CreateNote;

    public override IReadOnlyCollection<string> Frames => [TestNoteKinds.CreateBook];

    protected override Task<string?> Check(CreateTestNoteArgs args, AssistantContext context, CancellationToken ct) =>
        Task.FromResult(store.Notes.Values.Any(x => x.Title == args.Title)
            ? $"заметка «{args.Title}» уже есть"
            : null);

    protected override Task<string> Apply(CreateTestNoteArgs args, AssistantContext context, CancellationToken ct)
    {
        if (args.Title == TestNoteKinds.Refused)
            throw new BadRequestException(new ErrorMessage("test.error.refused", "раздел отказал"));

        var id = Guid.CreateVersion7();
        store.Notes[id] = new TestNoteEntry(args.Title, Guid.TryParse(args.BookLink, out var book) ? book : null);
        Interlocked.Increment(ref store.Writes);

        return Task.FromResult(id.ToString());
    }
}

/// <summary>
/// Завести книгу — рамка: одна на план и сразу не применяется никогда, её придумал ассистент
/// </summary>
public sealed class CreateTestBookOperation(TestNoteStore store) : PlanOperationHandler<CreateTestBookArgs>
{
    public override string Kind => TestNoteKinds.CreateBook;

    public override bool CanApplyImmediately => false;

    public override int? MaxPerPlan => 1;

    protected override Task<string> Apply(CreateTestBookArgs args, AssistantContext context, CancellationToken ct)
    {
        if (args.Title == TestNoteKinds.Refused)
            throw new NotFoundException(new ErrorMessage("test.error.refused", "раздел отказал"));

        var id = Guid.CreateVersion7();
        store.Books[id] = args.Title;
        Interlocked.Increment(ref store.Writes);

        return Task.FromResult(id.ToString());
    }
}

/// <summary>
/// Переименовать заметку — правка с целью и своим правом: в тестах право раздела
/// изображает <see cref="PermissionEnum.WorkspaceInfoCrud"/>
/// </summary>
public sealed class RenameTestNoteOperation(TestNoteStore store) : PlanOperationHandler<RenameTestNoteArgs>
{
    public override string Kind => TestNoteKinds.RenameNote;

    public override bool IsCreating => false;

    public override IReadOnlyList<PermissionEnum> Permissions => [PermissionEnum.WorkspaceInfoCrud];

    protected override string? Target(RenameTestNoteArgs args) => args.NoteId;

    protected override Task<string?> Check(RenameTestNoteArgs args, AssistantContext context, CancellationToken ct) =>
        Task.FromResult(Guid.TryParse(args.NoteId, out var id) && store.Notes.ContainsKey(id)
            ? null
            : "заметки уже нет");

    protected override Task<string> Apply(RenameTestNoteArgs args, AssistantContext context, CancellationToken ct)
    {
        var id = Guid.Parse(args.NoteId);
        store.Notes[id] = store.Notes[id] with { Title = args.Title };
        Interlocked.Increment(ref store.Writes);

        return Task.FromResult(args.NoteId);
    }
}
