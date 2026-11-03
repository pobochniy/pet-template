using NpgsqlTypes;

namespace PetTemplate.Tests.Features.Search.TestNotes;

/// <summary>
/// Тестовая сущность с текстом (спец. search, 9): в ядре шаблона сущностей нет, и механизм
/// поиска проверяется на ней — как на разделе проекта, который его подключил.
/// Живёт только в тестовом проекте, в своём контексте (<see cref="TestNotesContext"/>)
/// </summary>
public class TestNote
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    /// <summary>Вычисляемая колонка полнотекстового индекса — её пишет база (<c>FullTextSearch</c>)</summary>
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}
