using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Commands;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Компакция длинного контекста (спец. assistant, 12.4): в модель едут резюме плюс
/// последние N, а в ленте у человека остаётся вся переписка
/// </summary>
public class CompactionTests
{
    /// <summary>
    /// Пока порог не достигнут, компакция не запускается вовсе: в треде из коротких
    /// просьб её не бывает никогда
    /// </summary>
    [Fact]
    public async Task ShortThreadIsNotCompacted()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "как пригласить коллегу?", "UTC", CancellationToken.None);

        // Act
        await world.Handler(new FakeChatClient().Says("По справке — так."))
            .Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None);

        // Assert
        Assert.Empty(await QueueAssert.Commands<AssistantCompactionCommand>(world.Db));
    }

    /// <summary>
    /// Переписка переросла порог — сжатие ставится командой, а не делается в ходе:
    /// иначе один невезучий человек ждёт лишние секунды за всех
    /// </summary>
    [Fact]
    public async Task LongThreadQueuesCompaction()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        world.Settings.CompactionThreshold = 200;
        for (var i = 0; i < 10; i++)
            world.Message(AssistantMessageRole.User, new string('я', 50));
        await world.Save();
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId,
            "а теперь подбей итоги", "UTC", CancellationToken.None);

        // Act
        await world.Handler(new FakeChatClient().Says("Готово."))
            .Handle(new AssistantRunCommand { RunId = runId }, CancellationToken.None);

        // Assert
        var command = Assert.Single(await QueueAssert.Commands<AssistantCompactionCommand>(world.Db));
        Assert.Equal(world.Author, command.UserId);
        Assert.Equal(world.WorkspaceId, command.WorkspaceId);
    }

    /// <summary>После сжатия в модель едут резюме и последние N, а не весь тред (12.4)</summary>
    [Fact]
    public async Task ModelSeesSummaryAndLastMessages()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        world.Settings.HistoryMessages = 2;
        for (var i = 0; i < 8; i++)
            world.Message(i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant, $"реплика {i}");
        await world.Save();
        var chat = new FakeChatClient().Says("Говорили про поездку в горы, домик забронирован, id 17.");

        // Act
        await world.Compaction(chat).Handle(Command(world), CancellationToken.None);

        // Assert
        var history = await new HistoryBuilder(world.Db, Options.Create(world.Settings))
            .Build(world.Author, world.WorkspaceId, CancellationToken.None);
        Assert.Contains("поездку в горы", history.Summary);
        Assert.Equal(["реплика 6", "реплика 7"], history.Messages.Select(x => x.Text));
        // Сворачивается всё, кроме последних N: их модель и так увидит
        Assert.Contains("реплика 5", chat.Asked.Single()[0].Text);
        Assert.DoesNotContain("реплика 6", chat.Asked.Single()[0].Text);
    }

    /// <summary>
    /// Исходные сообщения остаются в базе и в ленте — человек видит свою переписку
    /// целиком. Само резюме в ленте не показывается: оно служебное
    /// </summary>
    [Fact]
    public async Task FeedKeepsEverythingButTheSummary()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        world.Settings.HistoryMessages = 2;
        for (var i = 0; i < 6; i++)
            world.Message(i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant, $"реплика {i}");
        await world.Save();
        await world.Compaction(new FakeChatClient().Says("Короткое резюме."))
            .Handle(Command(world), CancellationToken.None);

        // Act
        var feed = await world.Threads.History(world.Author, world.WorkspaceId, null, CancellationToken.None);

        // Assert
        Assert.Equal(6, feed.Messages.Count);
        Assert.DoesNotContain(feed.Messages, x => x.Role == AssistantMessageRole.Summary);
        Assert.Equal(7, await world.Db.AssistantMessages.CountAsync(CancellationToken.None));
    }

    /// <summary>Команда повторилась — второго резюме не появляется: сворачивать уже нечего</summary>
    [Fact]
    public async Task RepeatedCompactionWritesNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Крылышки и Ножки");
        world.Settings.HistoryMessages = 2;
        for (var i = 0; i < 6; i++)
            world.Message(i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant, $"реплика {i}");
        await world.Save();
        await world.Compaction(new FakeChatClient().Says("Первое резюме."))
            .Handle(Command(world), CancellationToken.None);
        var chat = new FakeChatClient().Says("Второе резюме.");

        // Act
        await world.Compaction(chat).Handle(Command(world), CancellationToken.None);

        // Assert
        Assert.Equal(0, chat.Calls);
        Assert.Equal(1, await world.Db.AssistantMessages
            .CountAsync(x => x.Role == AssistantMessageRole.Summary, CancellationToken.None));
    }

    /// <summary>Ходить некуда — тред подождёт следующей команды, резюме не пишется</summary>
    [Fact]
    public async Task CompactionWithoutModelWritesNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Тапки и Носки");
        world.Settings.HistoryMessages = 2;
        for (var i = 0; i < 6; i++)
            world.Message(i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant, $"реплика {i}");
        await world.Save();
        await world.Db.AiModels.ExecuteUpdateAsync(x => x.SetProperty(m => m.IsEnabled, false), CancellationToken.None);
        var chat = new FakeChatClient().Says("Резюме.");

        // Act
        await world.Compaction(chat).Handle(Command(world), CancellationToken.None);

        // Assert
        Assert.Equal(0, chat.Calls);
        Assert.Equal(0, await world.Db.AssistantMessages
            .CountAsync(x => x.Role == AssistantMessageRole.Summary, CancellationToken.None));
    }

    private static AssistantCompactionCommand Command(AssistantWorld world) => new()
    {
        UserId = world.Author,
        WorkspaceId = world.WorkspaceId
    };
}
