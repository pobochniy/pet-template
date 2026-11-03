using PetTemplate.Db.Extensions;
using PetTemplate.Services.Search;

namespace PetTemplate.Tests.Features.Search;

/// <summary>
/// Слияние выдач по RRF (спец. search, 7) — общее у поиска по сущностям и у справки:
/// позиция в каждой выдаче важнее её абсолютного счёта
/// </summary>
public class HybridSearchUnitTests
{
    /// <summary>Найденное обоими способами обгоняет первое место одной выдачи</summary>
    [Fact]
    public void FoundByBothBeatsTopOfOne()
    {
        // Act
        var ranked = HybridSearch.Ranked(10, ["вектор", "оба"], ["слово", "оба"]);

        // Assert
        Assert.Equal("оба", ranked[0]);
    }

    /// <summary>При равном счёте больший ключ выше: порядок не зависит от словаря</summary>
    [Fact]
    public void TieGoesToGreaterKey()
    {
        // Act
        var ranked = HybridSearch.Ranked(10, [1L], [2L]);

        // Assert
        Assert.Equal([2L, 1L], ranked);
    }

    [Fact]
    public void RespectsLimit()
    {
        // Act
        var ranked = HybridSearch.Ranked(2, [1, 2, 3, 4]);

        // Assert
        Assert.Equal([1, 2], ranked);
    }

    /// <summary>Счёт — сумма обратных позиций с k = 60</summary>
    [Fact]
    public void MergeSumsReciprocalRanks()
    {
        // Act
        var scores = HybridSearch.Merge(["a", "b"], ["b"]);

        // Assert
        Assert.Equal(1d / 61, scores["a"], 10);
        Assert.Equal(1d / 62 + 1d / 61, scores["b"], 10);
    }

    /// <summary>Запрос обрезается по краям и до двухсот символов; пустой — пустой</summary>
    [Fact]
    public void NormalizeTrimsAndCuts()
    {
        // Act & Assert
        Assert.Equal(string.Empty, HybridSearch.Normalize("   "));
        Assert.Equal("кран", HybridSearch.Normalize("  кран "));
        Assert.Equal(HybridSearch.MaxQueryLength, HybridSearch.Normalize(new string('я', 500)).Length);
    }

    /// <summary>Полнотекстовый запрос — «любое из слов» (<c>websearch_to_tsquery</c>)</summary>
    [Fact]
    public void AnyWordJoinsWithOr()
    {
        // Act & Assert
        Assert.Equal("кран or забор", FullTextSearch.AnyWord(" кран   забор "));
    }
}
