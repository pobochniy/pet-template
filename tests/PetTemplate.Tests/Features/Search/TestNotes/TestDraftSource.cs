using PetTemplate.Shared.Enums;

namespace PetTemplate.Tests.Features.Search.TestNotes;

/// <summary>
/// Второй тип над той же таблицей — чтобы проверить, что коллекция одна, а типы
/// в ней не смешиваются (идентификаторы у них нарочно общие)
/// </summary>
public class TestDraftSource(TestNotesContext db) : TestNoteSource(db)
{
    public const SearchEntityType DraftType = (SearchEntityType)1002;

    public override SearchEntityType Type => DraftType;
}
