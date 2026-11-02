using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Tests.Features.Search.TestNotes;

namespace PetTemplate.Tests.Features.Search;

/// <summary>
/// Полнотекстовая половина (спец. search, 7) — <c>tsvector</c> с GIN-индексом, заведённые
/// хелпером <c>FullTextSearch.HasFullTextSearch</c> на тестовой сущности. Эмбеддера нет:
/// проверяется только то, что находит сама база
/// </summary>
public class FullTextSearchTests
{
    private static readonly Guid RogaId = Guid.CreateVersion7();

    /// <summary>Колонку пишет база и держит GIN-индекс: без него поиск по словам — полный проход</summary>
    [Fact]
    public async Task HelperCreatesGinIndex()
    {
        // Arrange
        var world = await SearchWorld.Start(withEmbedder: false);
        using var scope = world.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestNotesContext>();

        // Act
        var indexes = await db.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'test_notes'")
            .ToListAsync();

        // Assert
        Assert.Contains(indexes, x => x.Contains("USING gin (search_vector)"));
    }

    /// <summary>Словарь <c>russian</c> сводит слово к основе: «крана» находит «кран»</summary>
    [Fact]
    public async Task FindsOtherWordForms()
    {
        // Arrange
        var world = await SearchWorld.Start(withEmbedder: false);
        var kran = SearchWorld.Note(RogaId, "Починить кран", "Течёт на кухне");
        await world.Add(kran);

        // Act
        var found = await world.Words(RogaId, "крана");

        // Assert
        Assert.Equal([kran.Id], found);
    }

    /// <summary>
    /// «Любое из слов», как natural language у MySQL в Lad и ASU: запись с одним словом
    /// из запроса находится, а не отсеивается, как при «все слова сразу»
    /// </summary>
    [Fact]
    public async Task FindsAnyOfWords()
    {
        // Arrange
        var world = await SearchWorld.Start(withEmbedder: false);
        var fence = SearchWorld.Note(RogaId, "Дача", "Покрасить забор");
        await world.Add(fence);

        // Act
        var found = await world.Words(RogaId, "кран забор");

        // Assert
        Assert.Equal([fence.Id], found);
    }

    /// <summary>Ввод человека с кавычками и минусами не роняет запрос: синтаксиса у него нет</summary>
    [Fact]
    public async Task SurvivesOddInput()
    {
        // Arrange
        var world = await SearchWorld.Start(withEmbedder: false);
        var fence = SearchWorld.Note(RogaId, "Дача", "Покрасить забор");
        await world.Add(fence);

        // Act
        var found = await world.Words(RogaId, "\"забор -) ( or & !");

        // Assert
        Assert.Equal([fence.Id], found);
    }

    [Fact]
    public async Task StaysInWorkspace()
    {
        // Arrange
        var world = await SearchWorld.Start(withEmbedder: false);
        await world.Add(SearchWorld.Note(Guid.CreateVersion7(), "Починить кран", "Течёт у соседей"));

        // Act
        var found = await world.Words(RogaId, "кран");

        // Assert
        Assert.Empty(found);
    }
}
