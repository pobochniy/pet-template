using System.Collections.Concurrent;

namespace PetTemplate.Tests.Features.Assistant.TestPlan;

/// <summary>
/// Хранилище тестового раздела: книги и заметки в памяти (спец. assistant, 7.1).
/// Предметных сущностей в ядре нет — механизм плана проверяется на них, как поиск
/// на <c>TestNote</c>. Один на мир теста
/// </summary>
public sealed class TestNoteStore
{
    public ConcurrentDictionary<Guid, string> Books { get; } = new();

    public ConcurrentDictionary<Guid, TestNoteEntry> Notes { get; } = new();

    /// <summary>Сколько раз операции что-то записали — повтор не должен писать второй раз</summary>
    public int Writes;
}

/// <summary>Заметка тестового раздела: название и книга, в которой лежит</summary>
public sealed record TestNoteEntry(string Title, Guid? BookId);
