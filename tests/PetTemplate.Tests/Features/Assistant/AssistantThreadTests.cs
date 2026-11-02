using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Commands;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Тред, очередь ходов и остановка (спец. assistant, раздел 3 и 12)
/// </summary>
public class AssistantThreadTests
{
    /// <summary>
    /// Сообщение, прогон и команда уезжают в базу одним сохранением: либо есть и ход,
    /// и задание на него, либо ни того, ни другого (outbox)
    /// </summary>
    [Fact]
    public async Task SendStartsRunAndQueuesCommand()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");

        // Act
        var (runId, messageId) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "как пригласить коллегу?", "Europe/Moscow", CancellationToken.None);

        // Assert
        var run = await world.Db.AssistantRuns.SingleAsync(CancellationToken.None);
        Assert.Equal(runId, run.Id);
        Assert.Equal(AssistantRunStatus.Running, run.Status);
        Assert.Equal("Europe/Moscow", run.TimeZone);
        Assert.Equal("ru", run.Language);

        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.Equal(messageId, message.Id);
        Assert.Equal(AssistantMessageRole.User, message.Role);
        Assert.Equal(run.Id, message.RunId);
        // Реплика и прогон — одно событие и одна отметка времени
        Assert.Equal(run.StartedAtUtc, message.CreatedAtUtc);
        var command = Assert.Single(await QueueAssert.Commands<AssistantRunCommand>(world.Db));
        Assert.Equal(run.Id, command.RunId);
    }

    /// <summary>
    /// Новое сообщение во время работы ход не прерывает, а ждёт своей очереди:
    /// команда не ставится, и два цикла по одному треду не пойдут (раздел 3)
    /// </summary>
    [Fact]
    public async Task SecondMessageWaitsInsteadOfStartingRun()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        var (first, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "найди в справке про приглашения", "UTC", CancellationToken.None);

        // Act
        var (second, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "и про права тоже", "UTC", CancellationToken.None);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(1, await world.Db.AssistantRuns.CountAsync(CancellationToken.None));
        Assert.Single(await QueueAssert.Commands<AssistantRunCommand>(world.Db));
        var waiting = await world.Db.AssistantMessages
            .SingleAsync(x => x.Content.StartsWith("и про права"), CancellationToken.None);
        Assert.Null(waiting.RunId);
    }

    /// <summary>
    /// Отправки с разных вкладок наперегонки — живой ход у треда всё равно один:
    /// его держит частичный уникальный индекс, а проигравшие встают в очередь треда
    /// (раздел 3)
    /// </summary>
    [Fact]
    public async Task ConcurrentSendsKeepOneRunningRun()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        var contexts = Enumerable.Range(0, 8).Select(_ => world.NewContext()).ToArray();

        // Act
        var sent = await Task.WhenAll(contexts.Select((db, i) =>
            world.ThreadsOver(db)
                .Send(world.Author, world.WorkspaceId, $"реплика {i}", "UTC", CancellationToken.None)));

        // Assert
        var running = await world.Db.AssistantRuns
            .Where(x => x.Status == AssistantRunStatus.Running)
            .ToListAsync(CancellationToken.None);
        var run = Assert.Single(running);
        Assert.All(sent, x => Assert.Equal(run.Id, x.RunId));
        Assert.Equal(8, await world.Db.AssistantMessages.CountAsync(CancellationToken.None));
        Assert.Equal(1, await world.Db.AssistantMessages.CountAsync(x => x.RunId != null, CancellationToken.None));
        Assert.Single(await QueueAssert.Commands<AssistantRunCommand>(world.Db));

        foreach (var db in contexts) await db.DisposeAsync();
    }

    /// <summary>
    /// Ход кончился — обработчик сам ставит команду на первое ждущее сообщение треда:
    /// очередь треда держится на этом, а не на очереди команд (раздел 3)
    /// </summary>
    [Fact]
    public async Task HandlerStartsWaitingMessage()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "как пригласить коллегу?", "Asia/Almaty", CancellationToken.None);
        await world.Threads.Send(world.Author, world.WorkspaceId, "а как убрать?", "UTC", CancellationToken.None);
        var chat = new FakeChatClient().Says("По справке — так.");

        // Act
        await world.Handler(chat).Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None);

        // Assert
        Assert.Equal(2, await world.Db.AssistantRuns.CountAsync(CancellationToken.None));
        Assert.Equal(2, (await QueueAssert.Commands<AssistantRunCommand>(world.Db)).Count);
        var waiting = await world.Db.AssistantMessages
            .SingleAsync(x => x.Content.StartsWith("а как убрать"), CancellationToken.None);
        Assert.NotNull(waiting.RunId);
        Assert.NotEqual(runId, waiting.RunId);
        // Зона ждущей реплики — та, с которой человек писал минуту назад
        var next = await world.Db.AssistantRuns.SingleAsync(x => x.Id == waiting.RunId, CancellationToken.None);
        Assert.Equal("Asia/Almaty", next.TimeZone);
    }

    /// <summary>Повтор команды на законченном ходе не делает ничего: идемпотентность по RunId</summary>
    [Fact]
    public async Task RepeatedCommandOnFinishedRunDoesNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Крылышки и Ножки");
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "как пригласить коллегу?", "UTC", CancellationToken.None);
        await world.Handler(new FakeChatClient().Says("По справке — так."))
            .Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None);
        var answers = await world.Db.AssistantMessages.CountAsync(CancellationToken.None);
        var chat = new FakeChatClient();

        // Act
        await world.Handler(chat).Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None);

        // Assert
        Assert.Equal(0, chat.Calls);
        Assert.Equal(answers, await world.Db.AssistantMessages.CountAsync(CancellationToken.None));
        var run = await world.Db.AssistantRuns.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantRunStatus.Done, run.Status);
        Assert.Equal(1, run.Attempts);
    }

    /// <summary>
    /// Первая упавшая попытка уходит в повтор очереди, вторая — конец: ход Failed,
    /// человек получает строку в ленте, а не молчание (раздел 3, 12.3)
    /// </summary>
    [Fact]
    public async Task SecondFailedAttemptEndsRunWithText()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пряники и Вафли");
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "как пригласить коллегу?", "UTC", CancellationToken.None);
        var chat = new FakeChatClient { OnAsk = _ => throw new HttpRequestException("connection refused") };
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            world.Handler(chat).Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None));

        // Act
        await world.Handler(chat).Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None);

        // Assert
        var run = await world.Db.AssistantRuns.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantRunStatus.Failed, run.Status);
        Assert.Equal(AssistantStopReason.ProviderError, run.StopReason);
        Assert.Equal(2, run.Attempts);
        var answer = await world.Db.AssistantMessages
            .SingleAsync(x => x.Role == AssistantMessageRole.Assistant, CancellationToken.None);
        Assert.Equal(AssistantTexts.ProviderError, answer.Content);
    }

    /// <summary>
    /// Шаг продлевает аренду команды: ход в тридцать шагов не должен достаться второму
    /// циклу очереди, пока первый ещё думает (раздел 3)
    /// </summary>
    [Fact]
    public async Task EveryStepExtendsLease()
    {
        // Arrange
        var world = await AssistantWorld.Build("Тапки и Носки");
        var run = world.StartRun();
        await world.Save();
        var lease = new NoLease();
        var chat = new FakeChatClient()
            .Calling("search_help", new { query = "раз" })
            .Calling("search_help", new { query = "два" })
            .Says("Готово.");

        // Act
        await world.Runner(chat).Execute(run.Id, () => lease.Extend(TimeSpan.FromMinutes(10), CancellationToken.None),
            CancellationToken.None);

        // Assert
        Assert.Equal(2, lease.Extended);
    }

    /// <summary>
    /// «Остановить» ставит статус, и ход выходит на ближайшей проверке — она стоит
    /// перед каждым обращением к модели (раздел 3)
    /// </summary>
    [Fact]
    public async Task StopEndsRunOnNextCheck()
    {
        // Arrange
        var world = await AssistantWorld.Build("Винтики и Шпунтики");
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "собери план", "UTC", CancellationToken.None);
        var stopped = await world.Threads.Stop(runId, world.Author, world.WorkspaceId, CancellationToken.None);
        var chat = new FakeChatClient().Says("не дойдёт");

        // Act
        await world.Handler(chat).Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None);

        // Assert
        Assert.True(stopped);
        Assert.Equal(0, chat.Calls);
        var run = await world.Db.AssistantRuns.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantRunStatus.Stopped, run.Status);
        Assert.Equal(AssistantStopReason.UserStopped, run.StopReason);
        var answer = await world.Db.AssistantMessages
            .SingleAsync(x => x.Role == AssistantMessageRole.Assistant, CancellationToken.None);
        Assert.Equal(AssistantTexts.StoppedWithNothing, answer.Content);
    }

    /// <summary>
    /// Кнопка оборвала уже идущий запрос к модели: ход всё равно сдаёт строку в ленту
    /// и уходит в Stopped, а не в повтор очереди (раздел 3)
    /// </summary>
    [Fact]
    public async Task StopDuringModelCallFinishesRun()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ложки и Поварёшки");
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "собери план", "UTC", CancellationToken.None);
        var chat = new FakeChatClient();
        chat.OnAsk = _ =>
        {
            world.Threads.Stop(runId, world.Author, world.WorkspaceId, CancellationToken.None).GetAwaiter().GetResult();
            throw new OperationCanceledException();
        };

        // Act
        await world.Handler(chat).Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None);

        // Assert
        var run = await world.Db.AssistantRuns.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantRunStatus.Stopped, run.Status);
        Assert.NotNull(run.FinishedAtUtc);
        Assert.Single(await world.Db.AssistantMessages
            .Where(x => x.Role == AssistantMessageRole.Assistant)
            .ToListAsync(CancellationToken.None));
    }

    /// <summary>Чужой прогон отвечает так же, как несуществующий (раздел 14)</summary>
    [Fact]
    public async Task StopOfStrangersRunFindsNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Вилки и Ножи");
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "как пригласить коллегу?", "UTC", CancellationToken.None);

        // Act
        var stopped = await world.Threads.Stop(runId, Guid.CreateVersion7(), world.WorkspaceId,
            CancellationToken.None);

        // Assert
        Assert.False(stopped);
        var run = await world.Db.AssistantRuns.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantRunStatus.Running, run.Status);
    }

    /// <summary>
    /// План показан один раз, а отчёт — другой: «Готово» и галочки приезжают
    /// с сообщением после согласия, а в сообщении с предложением план остаётся таким,
    /// каким человек его прочитал (5.1). Иначе применение дорисовывало галочки задним
    /// числом и к предложению — в ленте выходили два одинаковых блока
    /// </summary>
    [Fact]
    public async Task AppliedPlanReportsOnlyInTheMessageAfterConsent()
    {
        // Arrange
        var world = await AssistantWorld.Build("Сушки и Баранки");
        var plan = world.Plan(AssistantWorld.CreateNote("Забронировать домик"));
        world.Message(AssistantMessageRole.User, "запиши про поездку");
        var proposal = world.Message(AssistantMessageRole.Assistant, "Вот что предлагаю. Делаем?", plan.Id);
        world.Message(AssistantMessageRole.User, "давай");
        await world.Save();
        await world.Apply(plan);
        var report = world.Message(AssistantMessageRole.Assistant, "Записал", plan.Id);
        await world.Save();

        // Act
        var feed = await world.Threads.History(world.Author, world.WorkspaceId, null, CancellationToken.None);

        // Assert
        var shown = feed.Messages.Single(x => x.Id == proposal.Id);
        Assert.Equal(AssistantPlanStatus.Pending, shown.Plan!.Status);
        Assert.Null(shown.Plan.Result);
        Assert.Null(shown.Plan.AppliedAtUtc);

        var done = feed.Messages.Single(x => x.Id == report.Id);
        Assert.Equal(AssistantPlanStatus.Applied, done.Plan!.Status);
        Assert.NotNull(done.Plan.AppliedAtUtc);
        Assert.All(done.Plan.Result!.Operations, x => Assert.True(x.Done));
    }

    /// <summary>
    /// Отменённый план остаётся отменённым в своём же сообщении: человек видит,
    /// что предлагалось и что это не сделано (5.1)
    /// </summary>
    [Fact]
    public async Task CancelledPlanStaysCancelledInItsMessage()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ириски и Тянучки");
        var plan = world.Plan(AssistantPlanStatus.Cancelled, AssistantWorld.CreateNote("Купить билеты"));
        var proposal = world.Message(AssistantMessageRole.Assistant, "Вот что предлагаю. Делаем?", plan.Id);
        await world.Save();

        // Act
        var feed = await world.Threads.History(world.Author, world.WorkspaceId, null, CancellationToken.None);

        // Assert
        var shown = feed.Messages.Single(x => x.Id == proposal.Id);
        Assert.Equal(AssistantPlanStatus.Cancelled, shown.Plan!.Status);
    }

    /// <summary>
    /// Лента листается вверх курсором: страница — последние N, курсор — самое старое
    /// сообщение страницы, следующая — то, что старше него (раздел 14)
    /// </summary>
    [Fact]
    public async Task HistoryPagesByCursor()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пирожки и Ватрушки");
        world.Settings.HistoryPageSize = 3;
        for (var i = 0; i < 7; i++) world.Message(AssistantMessageRole.User, $"реплика {i}");
        await world.Save();
        var first = await world.Threads.History(world.Author, world.WorkspaceId, null, CancellationToken.None);
        var second = await world.Threads.History(world.Author, world.WorkspaceId, first.NextCursor, CancellationToken.None);

        // Act
        var third = await world.Threads.History(world.Author, world.WorkspaceId, second.NextCursor, CancellationToken.None);

        // Assert
        Assert.Equal(["реплика 4", "реплика 5", "реплика 6"], first.Messages.Select(x => x.Content));
        Assert.Equal(["реплика 1", "реплика 2", "реплика 3"], second.Messages.Select(x => x.Content));
        Assert.Equal(["реплика 0"], third.Messages.Select(x => x.Content));
        Assert.Null(third.NextCursor);
    }

    /// <summary>
    /// Снос пространства уносит и разговоры с ассистентом: это его содержимое,
    /// каскада от пространства нет (спец. authorization, 8)
    /// </summary>
    [Fact]
    public async Task WorkspacePurgeRemovesThread()
    {
        // Arrange
        var world = await AssistantWorld.Build("Котики и Собачки");
        var plan = world.Plan(AssistantWorld.CreateNote("Купить корм"));
        world.Message(AssistantMessageRole.Assistant, "Делаем?", plan.Id);
        world.StartRun();
        await world.Save();

        // Act
        await new WorkspaceService(world.Db).Leave(world.Author, world.WorkspaceId, CancellationToken.None);

        // Assert
        Assert.False(await world.Db.Workspaces.AnyAsync(x => x.Id == world.WorkspaceId, CancellationToken.None));
        Assert.False(await world.Db.AssistantMessages.AnyAsync(CancellationToken.None));
        Assert.False(await world.Db.AssistantPlans.AnyAsync(CancellationToken.None));
        Assert.False(await world.Db.AssistantRuns.AnyAsync(CancellationToken.None));
    }

    /// <summary>
    /// Удаление аккаунта уносит личное: тред, планы и прогоны человека (спец. accounts, 10)
    /// </summary>
    [Fact]
    public async Task UserDeletionCascadesThread()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёлки и Палки");
        world.Message(AssistantMessageRole.User, "привет");
        world.StartRun();
        await world.Save();
        await world.Db.UserInPermissions.Where(x => x.UserId == world.Author).ExecuteDeleteAsync(CancellationToken.None);

        // Act
        await world.Db.Users.Where(x => x.Id == world.Author).ExecuteDeleteAsync(CancellationToken.None);

        // Assert
        Assert.False(await world.Db.AssistantMessages.AnyAsync(CancellationToken.None));
        Assert.False(await world.Db.AssistantRuns.AnyAsync(CancellationToken.None));
    }

    /// <summary>Повтор обработчика на чужом прогоне ничего не трогает — повтора и нет, ход не найден</summary>
    [Fact]
    public async Task UnknownRunCommandDoesNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Гвозди и Шурупы");
        var chat = new FakeChatClient();

        // Act
        await world.Handler(chat).Handle(new AssistantRunCommand { RunId = Guid.CreateVersion7() }, CancellationToken.None);

        // Assert
        Assert.Equal(0, chat.Calls);
        Assert.False(await world.Db.AssistantMessages.AnyAsync(CancellationToken.None));
    }

    /// <summary>Индекс «один живой ход на тред» назван так, как его узнаёт <c>DbErrors</c></summary>
    [Fact]
    public async Task SecondRunningRunIsRejectedByIndex()
    {
        // Arrange
        var world = await AssistantWorld.Build("Чашки и Блюдца");
        world.StartRun();
        await world.Save();
        await using var other = world.NewContext();
        other.AssistantRuns.Add(new PetTemplate.Db.Entity.AssistantRun
        {
            Id = Guid.CreateVersion7(),
            UserId = world.Author,
            WorkspaceId = world.WorkspaceId,
            TimeZone = "UTC",
            Language = "ru",
            Status = AssistantRunStatus.Running,
            StartedAtUtc = world.Now
        });

        // Act
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.True(DbErrors.IsDuplicate(error, DbErrors.AssistantRunningIndex));
    }
}
