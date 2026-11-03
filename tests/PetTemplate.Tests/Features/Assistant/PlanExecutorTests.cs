using Microsoft.EntityFrameworkCore;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Features.Assistant.TestPlan;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Применение плана (спец. assistant, 5.1 и 5.4): исполняется сохранённый JSON слово
/// в слово, идемпотентно по плану и по операции, со сверкой с базой и живыми правами.
/// Операции — тестового раздела (<c>TestPlan</c>)
/// </summary>
public class PlanExecutorTests
{
    /// <summary>Применяется ровно то, что сохранено, и результат каждой операции записан</summary>
    [Fact]
    public async Task AppliesExactlySavedJson()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        var plan = world.Plan(AssistantWorld.CreateNote("Хлеб"), AssistantWorld.CreateNote("Молоко", 2));
        await world.Save();

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        Assert.Equal(["Молоко", "Хлеб"], world.Notes.Notes.Values.Select(x => x.Title).Order(StringComparer.Ordinal));
        var stored = await Stored(world, plan);
        Assert.Equal(AssistantPlanStatus.Applied, stored.Status);
        Assert.NotNull(stored.AppliedAtUtc);
        var result = PlanJson.Deserialize<PlanResult>(stored.Result!)!;
        Assert.All(result.Operations, x => Assert.True(x.Done));
        Assert.All(result.Operations, x => Assert.NotNull(x.CreatedId));
    }

    /// <summary>
    /// Рамка создаётся первой, и запись получает её настоящий идентификатор вместо <c>#1</c>
    /// </summary>
    [Fact]
    public async Task FrameIsCreatedFirstAndLinkGetsItsId()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        var plan = world.Plan(AssistantWorld.CreateBook("Рецепты"), AssistantWorld.CreateNote("Шарлотка", 2, "#1"));
        await world.Save();

        // Act
        await world.Apply(plan);

        // Assert
        var book = Assert.Single(world.Notes.Books);
        var note = Assert.Single(world.Notes.Notes.Values);
        Assert.Equal(book.Key, note.BookId);
    }

    /// <summary>Второе «да» вторых записей не создаёт, а отвечает уже сделанным (5.1)</summary>
    [Fact]
    public async Task SecondApplyCreatesNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        var plan = world.Plan(AssistantWorld.CreateNote("Хлеб"));
        await world.Save();
        await world.Apply(plan);

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        Assert.Equal(ApplyOutcomeKind.AlreadyApplied, outcome.Kind);
        Assert.Single(outcome.Result!.Operations);
        Assert.Equal(1, world.Notes.Writes);
    }

    /// <summary>
    /// Повтор после падения посреди плана: операции с результатом пропускаются, и сверка
    /// не находит «такая уже есть» — ту самую, что создала первая попытка (5.1)
    /// </summary>
    [Fact]
    public async Task RetrySkipsOperationsWithResult()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        var plan = world.Plan(AssistantWorld.CreateNote("Хлеб"), AssistantWorld.CreateNote("Молоко", 2));
        var done = world.SeedNote("Хлеб");
        plan.Result = PlanJson.Serialize(new PlanResult
        {
            Operations = [new PlanOperationResult { Index = 1, Done = true, CreatedId = done.ToString() }]
        });
        await world.Save();

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        Assert.Equal(1, world.Notes.Writes);
        Assert.Equal(2, world.Notes.Notes.Count);
        Assert.Equal(2, outcome.Result!.Operations.Count);
    }

    /// <summary>
    /// Права проверяются живыми данными при применении: у кого нет права раздела,
    /// у того операция получает крестик, а не исключение (раздел 9)
    /// </summary>
    [Fact]
    public async Task MissingPermissionRefusesOperation()
    {
        // Arrange
        var world = await AssistantWorld.Build("Крылышки и Ножки", PermissionEnum.WorkspaceMemberRead);
        var note = world.SeedNote("Старое");
        var plan = world.Plan(AssistantWorld.RenameNote(note, "Новое"));
        await world.Save();

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        var refused = Assert.Single(outcome.Result!.Operations);
        Assert.False(refused.Done);
        Assert.Contains("нет права", refused.Error);
        Assert.Equal("Старое", world.Notes.Notes[note].Title);
    }

    /// <summary>
    /// План разъехался с базой — не применяется <b>ничего</b>, а модель получает
    /// расхождения (5.4)
    /// </summary>
    [Fact]
    public async Task MismatchAppliesNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Винтики и Шпунтики");
        var plan = world.Plan(AssistantWorld.CreateNote("Хлеб"), AssistantWorld.RenameNote(Guid.CreateVersion7(), "Новое", 2));
        await world.Save();

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Mismatch, outcome.Kind);
        Assert.Contains("№2: заметки уже нет", outcome.Mismatches);
        Assert.Empty(world.Notes.Notes);
        Assert.Equal(AssistantPlanStatus.Pending, (await Stored(world, plan)).Status);
    }

    /// <summary>Автора убрали из пространства, пока он думал, — план не применяется (5.4)</summary>
    [Fact]
    public async Task RemovedAuthorAppliesNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Вилки и Ножи");
        var plan = world.Plan(AssistantWorld.CreateNote("Хлеб"));
        await world.Save();
        await world.Db.UserInPermissions.Where(x => x.UserId == world.Author).ExecuteDeleteAsync(CancellationToken.None);

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Mismatch, outcome.Kind);
        Assert.Contains("автора просьбы больше нет в этом пространстве", outcome.Mismatches);
        Assert.Empty(world.Notes.Notes);
    }

    /// <summary>
    /// Отказ сервиса раздела — крестик у своей операции, остальные идут дальше (5.1)
    /// </summary>
    [Fact]
    public async Task ServiceRefusalStopsOnlyItsOwnOperation()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ложки и Поварёшки");
        var plan = world.Plan(AssistantWorld.CreateNote(TestNoteKinds.Refused), AssistantWorld.CreateNote("Молоко", 2));
        await world.Save();

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        var result = outcome.Result!.Operations;
        Assert.False(result[0].Done);
        Assert.Equal("раздел отказал", result[0].Error);
        Assert.True(result[1].Done);
    }

    /// <summary>
    /// Рамка не создалась — запись, которая в неё ложилась, не делается вовсе:
    /// класть её «куда-нибудь» нельзя (5.1)
    /// </summary>
    [Fact]
    public async Task BrokenFrameRefusesDependents()
    {
        // Arrange
        var world = await AssistantWorld.Build("Чашки и Блюдца");
        var plan = world.Plan(AssistantWorld.CreateBook(TestNoteKinds.Refused), AssistantWorld.CreateNote("Шарлотка", 2, "#1"));
        await world.Save();

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        var result = outcome.Result!.Operations;
        Assert.False(result[0].Done);
        Assert.False(result[1].Done);
        Assert.Contains("не выполнена операция №1", result[1].Error);
        Assert.Empty(world.Notes.Notes);
    }

    /// <summary>Отменённый план не применяется ни повтором, ни поздним «да» (5.4)</summary>
    [Fact]
    public async Task CancelledPlanIsNotApplied()
    {
        // Arrange
        var world = await AssistantWorld.Build("Кастрюли и Сковородки");
        var plan = world.Plan(AssistantPlanStatus.Cancelled, AssistantWorld.CreateNote("Хлеб"));
        await world.Save();

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Cancelled, outcome.Kind);
        Assert.Empty(world.Notes.Notes);
    }

    /// <summary>Чужой план — как несуществующий (раздел 14)</summary>
    [Fact]
    public async Task StrangersPlanIsNotFound()
    {
        // Arrange
        var world = await AssistantWorld.Build("Тапки и Носки");
        var plan = world.Plan(AssistantWorld.CreateNote("Хлеб"));
        await world.Save();

        // Act
        var outcome = await world.Executor.Apply(plan.Id, Guid.CreateVersion7(), world.WorkspaceId, CancellationToken.None);

        // Assert
        Assert.Equal(ApplyOutcomeKind.NotFound, outcome.Kind);
        Assert.Empty(world.Notes.Notes);
    }

    /// <summary>
    /// JSON плана поправили в обход копилки — те же предохранители раздела 8 при
    /// применении: ничего не делается
    /// </summary>
    [Fact]
    public async Task TamperedPlanIsRevalidated()
    {
        // Arrange
        var world = await AssistantWorld.Build("Гвозди и Шурупы");
        var plan = world.Plan(AssistantWorld.CreateNote("Шарлотка", 1, "#2"), AssistantWorld.CreateBook("Рецепты", 2));
        await world.Save();

        // Act
        var outcome = await world.Apply(plan);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Mismatch, outcome.Kind);
        Assert.Empty(world.Notes.Books);
    }

    /// <summary>
    /// Два применяющих одного плана разом — повтор команды, пока первая ещё идёт: каждая
    /// операция делается один раз, второй ждёт замка плана и видит её сделанной (5.1)
    /// </summary>
    [Fact]
    public async Task ConcurrentApplyWritesEachOperationOnce()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пирожки и Ватрушки");
        var plan = world.Plan(AssistantWorld.CreateNote("Хлеб"), AssistantWorld.CreateNote("Молоко", 2));
        await world.Save();
        await using var first = world.NewContext();
        await using var second = world.NewContext();

        // Act
        var outcomes = await Task.WhenAll(
            Executor(world, first, world.Operations).Apply(plan.Id, world.Author, world.WorkspaceId, CancellationToken.None),
            Executor(world, second, world.Operations).Apply(plan.Id, world.Author, world.WorkspaceId, CancellationToken.None));

        // Assert
        Assert.Equal(2, world.Notes.Writes);
        Assert.Contains(outcomes, x => x.Kind == ApplyOutcomeKind.Applied);
        Assert.Equal(AssistantPlanStatus.Applied, (await Stored(world, plan)).Status);
    }

    /// <summary>
    /// Отказ операции откатывает то, что раздел успел записать до отказа: сделанной
    /// она не значится — значит, и записи от неё нет (5.1)
    /// </summary>
    [Fact]
    public async Task RefusedOperationRollsBackItsWrites()
    {
        // Arrange
        var world = await AssistantWorld.Build("Сырники и Оладьи");
        var plan = world.Plan(DbNote(TestNoteKinds.Refused));
        await world.Save();

        // Act
        var outcome = await Executor(world, world.Db, DbOperations(world.Db))
            .Apply(plan.Id, world.Author, world.WorkspaceId, CancellationToken.None);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        Assert.False(Assert.Single(outcome.Result!.Operations).Done);
        Assert.Empty(await Written(world));
    }

    /// <summary>
    /// Процесс умер между записью раздела и отметкой о ней в результате плана: запись
    /// уходит вместе с транзакцией, и повтор команды делает её ровно один раз (5.1)
    /// </summary>
    [Fact]
    public async Task InterruptedOperationIsAppliedOnceOnRetry()
    {
        // Arrange
        var world = await AssistantWorld.Build("Блины и Пышки");
        var plan = world.Plan(DbNote("Хлеб"));
        await world.Save();
        using var crash = new CancellationTokenSource();
        var operations = DbOperations(world.Db, afterWrite: crash.Cancel);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Executor(world, world.Db, operations)
            .Apply(plan.Id, world.Author, world.WorkspaceId, crash.Token));
        Assert.Empty(await Written(world));
        await using var retry = world.NewContext();
        var outcome = await Executor(world, retry, DbOperations(retry))
            .Apply(plan.Id, world.Author, world.WorkspaceId, CancellationToken.None);

        // Assert
        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        Assert.Single(await Written(world));
    }

    private static PlanExecutor Executor(AssistantWorld world, PetTemplate.Db.Entity.ApplicationContext db,
        PlanOperations operations) =>
        new(db, operations, new AssistantPermissions(db), world.Context);

    private static PlanOperations DbOperations(PetTemplate.Db.Entity.ApplicationContext db, Action? afterWrite = null) =>
        new([new DbNoteOperation(db, afterWrite)]);

    private static PlanOperation DbNote(string title) => new()
    {
        Index = 1,
        Kind = DbNoteOperation.Kind_,
        Args = PlanJson.ToArgs(new CreateTestNoteArgs { Title = title })
    };

    /// <summary>Что записала операция раздела — строки очереди с её названием</summary>
    private static Task<List<string>> Written(AssistantWorld world) =>
        world.NewContext().LocalQueue
            .Where(x => x.CommandType == CommandQueue.TypeOf<SearchIndexCommand>())
            .Select(x => x.Data)
            .ToListAsync(CancellationToken.None);

    /// <summary>
    /// Операция раздела, которая пишет в общую базу тем же контекстом, что и исполнитель
    /// плана, — как настоящий раздел. Пишет строку очереди: у неё нет внешних ключей
    /// </summary>
    private sealed class DbNoteOperation(PetTemplate.Db.Entity.ApplicationContext db, Action? afterWrite)
        : PlanOperationHandler<CreateTestNoteArgs>
    {
        public const string Kind_ = "create_db_note";

        public override string Kind => Kind_;

        protected override async Task<string> Apply(CreateTestNoteArgs args, AssistantContext context,
            CancellationToken ct)
        {
            db.Enqueue(new SearchIndexCommand { EntityType = SearchEntityType.None, EntityId = args.Title });
            await db.SaveChangesAsync(ct);
            afterWrite?.Invoke();

            if (args.Title == TestNoteKinds.Refused)
                throw new BadRequestException(new ErrorMessage("test.error.refused", "раздел отказал"));

            return Guid.CreateVersion7().ToString();
        }
    }

    private static Task<PetTemplate.Db.Entity.AssistantPlan> Stored(AssistantWorld world, PetTemplate.Db.Entity.AssistantPlan plan) =>
        world.Db.AssistantPlans.AsNoTracking().SingleAsync(x => x.Id == plan.Id, CancellationToken.None);
}
