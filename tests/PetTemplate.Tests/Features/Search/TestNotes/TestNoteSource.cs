using Microsoft.EntityFrameworkCore;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Tests.Features.Search.TestNotes;

/// <summary>
/// Источник текста тестовой сущности (спец. search, 9) — то, что раздел проекта пишет
/// к своему типу: заголовок первым, затем тело, и пространство записи
/// </summary>
public class TestNoteSource(TestNotesContext db) : ISearchSource
{
    /// <summary>
    /// Свой тип тестовой сущности. В ядре перечисление пустое (только <c>None</c>), значения
    /// разделов проекта дописываются в него; тестовому проекту дописать нечего, и его тип —
    /// число за пределами будущих значений шаблона
    /// </summary>
    public const SearchEntityType NoteType = (SearchEntityType)1001;

    public virtual SearchEntityType Type => NoteType;

    public async Task<SearchDocument?> Load(string entityId, CancellationToken ct)
    {
        if (!Guid.TryParse(entityId, out var id)) return null;

        var note = await db.Notes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

        return note == null ? null : new SearchDocument(note.WorkspaceId, $"# {note.Title}\n\n{note.Body}");
    }

    public async Task<IReadOnlyList<string>> AllIds(CancellationToken ct) =>
        await db.Notes.Select(x => x.Id.ToString()).ToListAsync(ct);
}
