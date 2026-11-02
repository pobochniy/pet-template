using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Billing;

/// <summary>
/// Лимит расхода ассистента на пространство (спец. assistant, 13.6): общий для всех
/// участников, проверяется на <c>Send</c> до старта хода, считает только своё
/// пространство; свой лимит в базе заменяет умолчание из конфигурации целиком
/// </summary>
public class WorkspaceLimitTests
{
    /// <summary>
    /// Лимит общий: Вася съел дневной потолок — Олегу в том же пространстве отказ
    /// кодом, и ни реплики, ни хода не появилось
    /// </summary>
    [Fact]
    public async Task LimitReachedByOneMemberRefusesAnother()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        world.SpendSettings.DefaultLimit.TokensPerDay = 1000;
        var vasya = await Member(world, "Vasya");
        world.Db.RunSpend.Add(Spend(world, vasya, world.Now, tokens: 1000));
        await world.Save();

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() =>
            world.Threads.Send(world.Author, world.WorkspaceId, "как пригласить?", "UTC", CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.DailyLimitReached.Code, refused.Code);
        Assert.Equal("Пончики и Плюшки", refused.Params!["workspace"]);
        var db = world.NewContext();
        Assert.False(await db.AssistantMessages.AnyAsync(CancellationToken.None));
        Assert.False(await db.AssistantRuns.AnyAsync(CancellationToken.None));
    }

    /// <summary>Расход другого пространства в лимит не идёт — даже того же человека</summary>
    [Fact]
    public async Task OtherWorkspaceSpendDoesNotCount()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        world.SpendSettings.DefaultLimit.TokensPerDay = 1000;
        var spend = Spend(world, world.Author, world.Now, tokens: 50_000);
        spend.WorkspaceId = Guid.CreateVersion7();
        world.Db.RunSpend.Add(spend);
        await world.Save();

        // Act
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId, "привет", "UTC",
            CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, runId);
    }

    /// <summary>
    /// Расход прошлых дней месяца в дневной лимит не идёт, а в месячный — идёт:
    /// месяц съеден, отказ — месячный. Даты свои, а не часы теста: на первое число
    /// «прошлых дней месяца» не бывает
    /// </summary>
    [Fact]
    public async Task MonthlyLimitCountsEarlierDays()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        world.SpendSettings.DefaultLimit.TokensPerDay = 1000;
        world.SpendSettings.DefaultLimit.CostPerMonth = 5m;
        var spend = Spend(world, world.Author, new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), tokens: 5000);
        spend.Cost = 5m;
        world.Db.RunSpend.Add(spend);
        await world.Save();

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() => world.Threads.CheckLimit(
            world.WorkspaceId, new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc), CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.MonthlyLimitReached.Code, refused.Code);
    }

    /// <summary>Прошлый месяц в месячный лимит не идёт</summary>
    [Fact]
    public async Task PreviousMonthDoesNotCount()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пряники и Вафли");
        world.SpendSettings.DefaultLimit.TokensPerMonth = 1000;
        world.Db.RunSpend.Add(Spend(world, world.Author, new DateTime(2026, 2, 28, 23, 0, 0, DateTimeKind.Utc), 5000));
        await world.Save();

        // Act
        var refused = await Record.ExceptionAsync(() => world.Threads.CheckLimit(
            world.WorkspaceId, new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc), CancellationToken.None));

        // Assert
        Assert.Null(refused);
    }

    /// <summary>
    /// Свой лимит пространства заменяет умолчание целиком: пустое поле в нём —
    /// «без потолка», даже если умолчание потолок ставит
    /// </summary>
    [Fact]
    public async Task OwnLimitReplacesDefault()
    {
        // Arrange
        var world = await AssistantWorld.Build("Котики и Собачки");
        world.SpendSettings.DefaultLimit.TokensPerDay = 1000;
        world.Db.WorkspaceAssistantLimits.Add(new WorkspaceAssistantLimit
        {
            WorkspaceId = world.WorkspaceId,
            TokensPerMonth = 1_000_000,
            UpdatedUtc = world.Now
        });
        world.Db.RunSpend.Add(Spend(world, world.Author, world.Now, tokens: 5000));
        await world.Save();

        // Act
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId, "привет", "UTC",
            CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, runId);
    }

    /// <summary>
    /// Пока потолок не достигнут, ход идёт — и его расход ложится в лимит: следующий
    /// <c>Send</c> того же пространства уже получает отказ (лимит мягкий, 13.6)
    /// </summary>
    [Fact]
    public async Task FinishedRunSpendClosesTheLimit()
    {
        // Arrange
        var world = await AssistantWorld.Build("Винтики и Шпунтики");
        world.SpendSettings.DefaultLimit.TokensPerDay = 100;
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId, "привет", "UTC",
            CancellationToken.None);
        await world.Handler(new FakeChatClient().Says("Привет.").Costing(500, 50))
            .Handle(new Services.Commands.AssistantRunCommand { RunId = runId }, CancellationToken.None);

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() =>
            world.Threads.Send(world.Author, world.WorkspaceId, "ещё", "UTC", CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.DailyLimitReached.Code, refused.Code);
    }

    /// <summary>Отказ на <c>Send</c> — <c>400</c> с кодом словаря <c>assistant</c></summary>
    [Fact]
    public async Task SendAnswersLimitCode()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        world.Db.WorkspaceAssistantLimits.Add(new WorkspaceAssistantLimit
        {
            WorkspaceId = world.WorkspaceId,
            CostPerDay = 1m,
            UpdatedUtc = world.Now
        });
        var spend = Spend(world, world.Author, world.Now, tokens: 10);
        spend.Cost = 1m;
        world.Db.RunSpend.Add(spend);
        await world.Save();

        // Act
        var response = await world.Client.PostAsJsonAsync("/api/Assistant/Send",
            new AssistantSendDto { Text = "привет" }, CancellationToken.None);

        // Assert
        await response.ShouldBeBadRequestWith(AssistantErrors.DailyLimitReached);
    }

    private static async Task<Guid> Member(AssistantWorld world, string name)
    {
        var (_, profile) = new UserBuilder(name).WithAllPermissions(world.WorkspaceId).Please();
        world.Db.Profiles.Add(profile);
        await world.Save();

        return profile.Id;
    }

    private static RunSpend Spend(AssistantWorld world, Guid userId, DateTime at, long tokens) => new()
    {
        Id = Guid.CreateVersion7(),
        RunId = Guid.CreateVersion7(),
        WorkspaceId = world.WorkspaceId,
        UserId = userId,
        ModelId = world.Model.Id,
        TokensIn = tokens,
        CreatedUtc = at
    };
}
