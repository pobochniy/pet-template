using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Search;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.Features.Search.TestNotes;

namespace PetTemplate.Tests.Features.Search;

/// <summary>
/// Индексация и изоляция пространств в векторном поиске (спец. search, 4–7) — на тестовой
/// сущности (<see cref="TestNote"/>). Qdrant настоящий, в контейнере: проверяется именно
/// фильтр payload, а его знает только Qdrant. База — настоящий PostgreSQL: полнотекстовая
/// половина — <c>tsvector</c>
/// </summary>
public class SearchIndexWorker
{
    private static readonly Guid RogaId = Guid.CreateVersion7();

    /// <summary>
    /// <b>Главный тест фичи по цене ошибки.</b> Qdrant не знает про пространства:
    /// коллекция одна на всех, и вектор чужого пространства лежит рядом с вашим. Забытый
    /// фильтр здесь — утечка чужого содержимого.
    ///
    /// Тест падает, если из <c>QdrantService.Search</c> убрать фильтр по
    /// <c>workspaceId</c>: векторы обеих заметок одинаковы нарочно, и без фильтра
    /// поиск вернёт обе точки
    /// </summary>
    [Fact]
    public async Task SearchNeverLeavesItsWorkspace()
    {
        // Arrange: два пространства, у каждого своя заметка про одно и то же
        var world = await SearchWorld.Start();
        var kopytaId = Guid.CreateVersion7();
        var mine = SearchWorld.Note(RogaId, "Котёл", "Котёл меняли в две тысячи двадцать третьем");
        var stranger = SearchWorld.Note(kopytaId, "Котёл", "Котёл у соседей тоже меняли");
        await world.Add(mine, stranger);

        // Act
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, mine.Id));
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, stranger.Id));
        var found = await world.Points(RogaId);

        // Assert
        Assert.NotEmpty(found);
        Assert.All(found, x => Assert.Equal(mine.Id.ToString(), x.EntityId));
    }

    /// <summary>
    /// Гибридная выдача тоже не выходит из пространства: и векторная половина, и полнотекстовая
    /// фильтруют по нему каждая сама
    /// </summary>
    [Fact]
    public async Task HybridSearchNeverLeavesItsWorkspace()
    {
        // Arrange
        var world = await SearchWorld.Start();
        var mine = SearchWorld.Note(RogaId, "Котёл", "Гарантия на котёл");
        var stranger = SearchWorld.Note(Guid.CreateVersion7(), "Котёл", "Гарантия на котёл у соседей");
        await world.Add(mine, stranger);
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, mine.Id));
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, stranger.Id));

        // Act
        var found = await world.Find(RogaId, "котёл");

        // Assert
        Assert.Equal([mine.Id], found);
    }

    /// <summary>Правка переписывает чанки заметки целиком: обрывков прежнего текста в индексе не остаётся</summary>
    [Fact]
    public async Task EditReplacesChunks()
    {
        // Arrange
        var world = await SearchWorld.Start();
        var note = SearchWorld.Note(RogaId, "Закупка", "Бублики. Сушки. Баранки.");
        await world.Add(note);
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, note.Id));

        // Act
        await world.Rewrite(note.Id, "Только баранки.");
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, note.Id));

        // Assert
        var found = await world.Points(RogaId);
        Assert.NotEmpty(found);
        Assert.All(found, x => Assert.DoesNotContain("Бублики", x.Text));
    }

    /// <summary>
    /// Заметки больше нет — точки уходят вместе с ней: удаление приходит той же
    /// командой, и обработчик видит, что индексировать нечего
    /// </summary>
    [Fact]
    public async Task DeletedNoteLeavesNoPoints()
    {
        // Arrange
        var world = await SearchWorld.Start();
        var note = SearchWorld.Note(RogaId, "Черновик", "Что-то временное");
        await world.Add(note);
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, note.Id));

        // Act
        await world.Remove(note.Id);
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, note.Id));

        // Assert
        Assert.Empty(await world.Points(RogaId));
    }

    /// <summary>
    /// Типы лежат в одной коллекции и различаются полем в payload: «среди черновиков»
    /// не приносит заметок. Идентификатор нарочно один — точки у типов всё равно свои
    /// </summary>
    [Fact]
    public async Task TypesAreSeparated()
    {
        // Arrange
        var world = await SearchWorld.Start();
        var note = SearchWorld.Note(RogaId, "Починить кран", "Течёт на кухне");
        await world.Add(note);

        // Act
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, note.Id));
        await world.Handle(SearchIndex.Of(TestDraftSource.DraftType, note.Id));
        await world.Handle(SearchIndex.Of(TestDraftSource.DraftType, note.Id));

        // Assert: у черновика ровно один чанк — повтор команды не плодит точек
        var drafts = await world.Points(RogaId, TestDraftSource.DraftType);
        Assert.Equal(TestDraftSource.DraftType, Assert.Single(drafts).Type);
    }

    /// <summary>
    /// Чанки уходят эмбеддеру как документы (<c>passage: </c>), а не как запросы:
    /// у e5 векторы запроса и документа считаются с разными префиксами (спец. search, 3)
    /// </summary>
    [Fact]
    public async Task ChunksAreEmbeddedAsPassages()
    {
        // Arrange
        var world = await SearchWorld.Start();
        var note = SearchWorld.Note(RogaId, "Забор", "Красили прошлым летом");
        await world.Add(note);

        // Act
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, note.Id));

        // Assert
        Assert.All(world.Embedder.Calls, x => Assert.Equal(EmbeddingPurpose.Passage, x.Purpose));
    }

    /// <summary>
    /// Полный реиндекс (спец. search, 5) не индексирует сам, а ставит команду на каждую
    /// сущность каждого источника: обрыв стоит одной сущности, а не всего прохода
    /// </summary>
    [Fact]
    public async Task ReindexEnqueuesEveryEntity()
    {
        // Arrange
        var world = await SearchWorld.Start();
        var first = SearchWorld.Note(RogaId, "Первая", "Текст");
        var second = SearchWorld.Note(RogaId, "Вторая", "Текст");
        await world.Add(first, second);

        // Act
        await world.Handle(new SearchReindexCommand());

        // Assert: обе заметки — под обоими типами тестовых источников
        var queued = await QueueAssert.Commands<SearchIndexCommand>(world.Db());
        Assert.Equal(
            new[]
            {
                $"{TestNoteSource.NoteType}:{first.Id}", $"{TestNoteSource.NoteType}:{second.Id}",
                $"{TestDraftSource.DraftType}:{first.Id}", $"{TestDraftSource.DraftType}:{second.Id}"
            }.Order(),
            queued.Select(x => $"{x.EntityType}:{x.EntityId}").Order());
    }

    /// <summary>
    /// Заметка, найденная и вектором, и словом, стоит выше найденной одним способом:
    /// в RRF позиция в каждой выдаче важнее её абсолютного счёта (спец. search, 7)
    /// </summary>
    [Fact]
    public async Task NoteFoundByBothWaysWins()
    {
        // Arrange: вектор у обеих одинаковый — его выдачу решает порядок Qdrant, слово — одно
        var world = await SearchWorld.Start();
        var both = SearchWorld.Note(RogaId, "Ремонт", "Гарантия на котёл пять лет");
        var vectorOnly = SearchWorld.Note(RogaId, "Дача", "Про баню и забор");
        await world.Add(both, vectorOnly);
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, both.Id));
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, vectorOnly.Id));

        // Act
        var found = await world.Find(RogaId, "котёл");

        // Assert
        Assert.Equal([both.Id, vectorOnly.Id], found);
    }

    /// <summary>
    /// Эмбеддер лёг — поиск отвечает тем, что нашёл по словам, а не ошибкой: эмбеддер живёт
    /// в отдельном контейнере, и его отказ не должен ронять поиск (спец. search, 7)
    /// </summary>
    [Fact]
    public async Task FailingEmbedderLeavesWordSearch()
    {
        // Arrange
        var world = await SearchWorld.Start();
        var kran = SearchWorld.Note(RogaId, "Починить кран", "Течёт на кухне");
        await world.Add(kran);
        world.Embedder.IsDown = true;

        // Act
        var found = await world.Find(RogaId, "кран");

        // Assert
        Assert.Equal([kran.Id], found);
    }

    /// <summary>
    /// Эмбеддер лёг при индексации — команда падает, а не молча теряет сущность:
    /// очередь её повторит (спец. search, 4)
    /// </summary>
    [Fact]
    public async Task FailingEmbedderFailsIndexing()
    {
        // Arrange
        var world = await SearchWorld.Start();
        var note = SearchWorld.Note(RogaId, "Починить кран", "Течёт на кухне");
        await world.Add(note);
        world.Embedder.IsDown = true;

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            world.Handle(SearchIndex.Of(TestNoteSource.NoteType, note.Id)));
    }

    /// <summary>
    /// Эмбеддер не настроен — индекса нет, и это не ошибка: команда проходит,
    /// точек нет, поиск отвечает словами (спец. search, 3)
    /// </summary>
    [Fact]
    public async Task WithoutEmbedderSearchUsesWordsOnly()
    {
        // Arrange
        var world = await SearchWorld.Start(withEmbedder: false);
        var kran = SearchWorld.Note(RogaId, "Починить кран", "Течёт на кухне");
        await world.Add(kran);
        await world.Handle(SearchIndex.Of(TestNoteSource.NoteType, kran.Id));

        // Act
        var found = await world.Find(RogaId, "кран");

        // Assert
        Assert.Equal([kran.Id], found);
    }

    /// <summary>
    /// Весь путь: команда, поставленная в очередь тем же сохранением, что и правка (outbox),
    /// доходит через воркер до индекса — обработчик зарегистрирован в очереди
    /// </summary>
    [Fact]
    public async Task QueuedCommandReachesIndex()
    {
        // Arrange
        var world = await SearchWorld.Start(withQueueWorker: true);
        var note = SearchWorld.Note(RogaId, "Отпуск", "Билеты на поезд");
        await world.Add(note);
        var db = world.Db();

        // Act
        db.Enqueue(SearchIndex.Of(TestNoteSource.NoteType, note.Id));
        await db.SaveChangesAsync();

        // Assert
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while ((await world.Points(RogaId)).Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal(note.Id.ToString(), Assert.Single(await world.Points(RogaId)).EntityId);
    }
}
