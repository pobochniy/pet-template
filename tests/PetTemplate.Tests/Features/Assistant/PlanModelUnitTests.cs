using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Features.Assistant.TestPlan;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// План как данные (спец. assistant, 5.1, 7.1, 8, 12.2.1): формат JSON, предохранители
/// копилки и граница немедленного применения. База не нужна — только реестр операций
/// тестового раздела
/// </summary>
public class PlanModelUnitTests
{
    private readonly PlanOperations _registry = Registry();

    /// <summary>
    /// План переживает базу слово в слово: применяется сохранённый JSON, а не то,
    /// что модель сочинит в момент согласия (5.1). Вид операции — строкой, кириллица —
    /// как есть: его читают глазами и целиком отдают модели (12.4)
    /// </summary>
    [Fact]
    public void PlanSurvivesRoundTrip()
    {
        // Arrange
        var plan = new PlanDocument
        {
            Operations = [AssistantWorld.CreateBook("Рецепты"), AssistantWorld.CreateNote("Шарлотка", 2, "#1")]
        };

        // Act
        var json = PlanJson.Serialize(plan);
        var back = PlanJson.Deserialize<PlanDocument>(json)!;

        // Assert
        Assert.Contains("\"kind\":\"CreateTestBook\"", json);
        Assert.Contains("Шарлотка", json);
        Assert.Equal(2, back.Operations.Count);
        Assert.Equal("#1", PlanJson.FromArgs<CreateTestNoteArgs>(back.Operations[1].Args)!.BookLink);
    }

    /// <summary>
    /// Двадцать первое дело в плане — отказ строкой: сводку длиннее человек
    /// не дочитывает (раздел 8)
    /// </summary>
    [Fact]
    public void TwentyFirstEntityIsRejected()
    {
        // Arrange
        var collector = new PlanCollector(_registry);
        for (var i = 0; i < PlanCollector.MaxCreated; i++) Propose(collector, $"дело {i}");

        // Act
        var answer = Propose(collector, "лишнее");

        // Assert
        Assert.Contains("в плане уже 20 дел", answer);
        Assert.Equal(PlanCollector.MaxCreated, collector.Count);
    }

    /// <summary>Правка существующего ничего не создаёт и в потолок двадцати не идёт</summary>
    [Fact]
    public void UpdatesDoNotCountAsCreated()
    {
        // Arrange
        var collector = new PlanCollector(_registry);
        for (var i = 0; i < PlanCollector.MaxCreated; i++) Propose(collector, $"дело {i}");

        // Act
        var answer = collector.Propose(TestNoteKinds.RenameNote,
            new RenameTestNoteArgs { NoteId = Guid.CreateVersion7().ToString(), Title = "Новое" });

        // Assert
        Assert.Contains("записал в план под номером 21", answer);
        Assert.Equal(PlanCollector.MaxCreated, collector.CountCreated);
    }

    /// <summary>Свой потолок операции — «рамка одна на план» — держит копилка (7.1)</summary>
    [Fact]
    public void SecondFrameIsRejected()
    {
        // Arrange
        var collector = new PlanCollector(_registry);
        collector.Propose(TestNoteKinds.CreateBook, new CreateTestBookArgs { Title = "Рецепты" });

        // Act
        var answer = collector.Propose(TestNoteKinds.CreateBook, new CreateTestBookArgs { Title = "Ремонт" });

        // Assert
        Assert.Contains("в плане уже 1 операций «CreateTestBook»", answer);
        Assert.Equal(1, collector.Count);
    }

    /// <summary>Ссылка вперёд — отказ: циклов не бывает по построению (раздел 8)</summary>
    [Fact]
    public void ForwardReferenceIsRejected()
    {
        // Arrange
        var collector = new PlanCollector(_registry);

        // Act
        var answer = collector.Propose(TestNoteKinds.CreateNote, new CreateTestNoteArgs { Title = "Шарлотка", BookLink = "#2" });

        // Assert
        Assert.Contains("ведёт вперёд или в пустоту", answer);
        Assert.Equal(0, collector.Count);
    }

    /// <summary>Ссылаться можно только на свою рамку: заметка — на книгу, но не на заметку</summary>
    [Fact]
    public void ReferenceToNonFrameIsRejected()
    {
        // Arrange
        var collector = new PlanCollector(_registry);
        Propose(collector, "Первая");

        // Act
        var answer = collector.Propose(TestNoteKinds.CreateNote, new CreateTestNoteArgs { Title = "Вторая", BookLink = "#1" });

        // Assert
        Assert.Contains("ведёт не на свою рамку", answer);
    }

    /// <summary>
    /// «#2» в названии — текст человека, а не ссылка: ссылкой считается только поле
    /// <c>…Link</c>
    /// </summary>
    [Fact]
    public void ReferenceLikeTitleIsJustText()
    {
        // Arrange
        var collector = new PlanCollector(_registry);

        // Act
        var answer = Propose(collector, "#2");

        // Assert
        Assert.Contains("записал в план под номером 1", answer);
    }

    /// <summary>Неизвестного вида операции в плане не бывает: модель собирает из того, что есть</summary>
    [Fact]
    public void UnknownOperationIsRejected()
    {
        // Arrange
        var collector = new PlanCollector(_registry);

        // Act
        var answer = collector.Propose("DeleteEverything", new { });

        // Assert
        Assert.Contains("операции «DeleteEverything» нет", answer);
    }

    /// <summary>
    /// Названное человеком — применяется сразу; ничего не решал ассистент, читать
    /// нечего (5.1)
    /// </summary>
    [Fact]
    public void NamedNotesApplyImmediately()
    {
        // Arrange
        PlanOperation[] operations = [AssistantWorld.CreateNote("Хлеб"), AssistantWorld.CreateNote("Молоко", 2)];

        // Act
        var consent = PlanConsent.Required(operations, _registry, 5);

        // Assert
        Assert.Null(consent);
    }

    /// <summary>Рамку придумал ассистент — такой план идёт через «делаем?» всегда (5.1)</summary>
    [Fact]
    public void FrameNeedsConsent()
    {
        // Arrange
        PlanOperation[] operations = [AssistantWorld.CreateBook("Рецепты"), AssistantWorld.CreateNote("Шарлотка", 2, "#1")];

        // Act
        var consent = PlanConsent.Required(operations, _registry, 5);

        // Assert
        Assert.Contains("CreateTestBook сразу не применяется", consent);
    }

    /// <summary>Больше потолка затронутых — список читают до, а не после (5.1)</summary>
    [Fact]
    public void LongPlanNeedsConsent()
    {
        // Arrange
        var operations = Enumerable.Range(1, 6).Select(i => AssistantWorld.CreateNote($"дело {i}", i)).ToArray();

        // Act
        var consent = PlanConsent.Required(operations, _registry, 5);

        // Assert
        Assert.Contains("в плане 6 новых и изменённых дел", consent);
    }

    /// <summary>Ноль в настройке — немедленного применения нет вовсе (раздел 17)</summary>
    [Fact]
    public void ZeroLimitSendsEveryPlanThroughConsent()
    {
        // Act
        var consent = PlanConsent.Required([AssistantWorld.CreateNote("Хлеб")], _registry, 0);

        // Assert
        Assert.Contains("выключено настройкой", consent);
    }

    /// <summary>Десять правок одной записи — это одна запись, а не десять (5.1)</summary>
    [Fact]
    public void UpdatesOfOneTargetCountOnce()
    {
        // Arrange
        var note = Guid.CreateVersion7();
        var operations = Enumerable.Range(1, 6).Select(i => AssistantWorld.RenameNote(note, $"вариант {i}", i)).ToArray();

        // Act
        var consent = PlanConsent.Required(operations, _registry, 5);

        // Assert
        Assert.Null(consent);
    }

    private static string Propose(PlanCollector collector, string title) =>
        collector.Propose(TestNoteKinds.CreateNote, new CreateTestNoteArgs { Title = title });

    private static PlanOperations Registry()
    {
        var store = new TestNoteStore();

        return new PlanOperations(
        [
            new CreateTestNoteOperation(store),
            new CreateTestBookOperation(store),
            new RenameTestNoteOperation(store)
        ]);
    }
}
