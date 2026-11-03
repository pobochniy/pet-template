using PetTemplate.Services.Search;

namespace PetTemplate.Tests.Features.Search;

/// <summary>
/// Нарезка текста сущности на чанки (спец. search, 4). Индексируются чанки,
/// а не сущности целиком: всё, что не влезло в окно модели, обрезается молча —
/// и слово в третьем абзаце описания не нашлось бы никогда.
/// </summary>
public class SearchChunkerUnitTests
{
    /// <summary>Пустой текст — пустой индекс, а не один пустой чанк</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void EmptyTextGivesNoChunks(string markdown)
    {
        // Act
        var chunks = SearchChunker.Split(markdown, 400);

        // Assert
        Assert.Empty(chunks);
    }

    /// <summary>Короткий текст — один чанк: резать его незачем</summary>
    [Fact]
    public void ShortTextStaysWhole()
    {
        // Act
        var chunks = SearchChunker.Split("Котёл меняли в 2023 году. Мастера звали Олег.", 400);

        // Assert
        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Index);
        Assert.Contains("Котёл", chunk.Text);
    }

    /// <summary>
    /// Заголовок начинает новый чанк: под ним лежит своя тема, и склеивать её
    /// с хвостом предыдущей значит смешивать два ответа в одном
    /// </summary>
    [Fact]
    public void HeadingStartsNewChunk()
    {
        // Arrange
        var markdown = "# Котёл\n\nМеняли в 2023 году.\n\n# Забор\n\nКрасили прошлым летом.";

        // Act
        var chunks = SearchChunker.Split(markdown, 400);

        // Assert
        Assert.Equal(2, chunks.Count);
        Assert.Contains("Котёл", chunks[0].Text);
        Assert.Contains("Забор", chunks[1].Text);
        Assert.DoesNotContain("Забор", chunks[0].Text);
    }

    /// <summary>
    /// Длинный текст режется на куски по размеру, и номера идут подряд:
    /// по ним точка находит своё место
    /// </summary>
    [Fact]
    public void LongTextIsSplitIntoNumberedChunks()
    {
        // Arrange: двадцать абзацев по полсотни знаков
        var markdown = string.Join("\n\n", Enumerable.Range(0, 20)
            .Select(i => $"Абзац номер {i}, в нём написано что-то про ремонт дачи."));

        // Act
        var chunks = SearchChunker.Split(markdown, 40);

        // Assert
        Assert.True(chunks.Count > 1);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(x => x.Index));
        Assert.All(chunks, x => Assert.NotEmpty(x.Text));
    }

    /// <summary>
    /// Абзац длиннее чанка целиком — не теряется: потерять хвост молча хуже,
    /// чем разорвать фразу
    /// </summary>
    [Fact]
    public void HugeParagraphIsNotLost()
    {
        // Arrange
        var markdown = new string('я', 2000);

        // Act
        var chunks = SearchChunker.Split(markdown, 40);

        // Assert
        Assert.True(chunks.Count > 1);
        Assert.True(chunks.Sum(x => x.Text.Length) >= markdown.Length - chunks.Count);
    }
}
