using Microsoft.EntityFrameworkCore;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Commands;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Billing;

/// <summary>
/// Учёт расхода без баланса (спец. assistant, 13.5): ход пишет строку расхода
/// с пространством и человеком по цене своей модели, компакция — служебную строку,
/// и ни у кого ничего не списывается
/// </summary>
public class SpendTests
{
    /// <summary>
    /// Ход с токенами — одна строка расхода: пространство, кто спросил, модель, токены
    /// по всем шагам и стоимость по прайсу, кешированные — по своей ставке
    /// </summary>
    [Fact]
    public async Task RunWritesSpendWithWorkspaceAndUser()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        await Price(world, costIn: 2m, costCached: 0.5m, costOut: 10m);
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient()
            .Calling("search_help", new { query = "как пригласить" }).Costing(1000, 100, cached: 400)
            .Says("Так.").Costing(1000, 100);

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var spend = await world.NewContext().RunSpend.SingleAsync(CancellationToken.None);
        Assert.Equal(run.Id, spend.RunId);
        Assert.Equal(world.WorkspaceId, spend.WorkspaceId);
        Assert.Equal(world.Author, spend.UserId);
        Assert.Equal(world.Model.Id, spend.ModelId);
        Assert.Equal(2000, spend.TokensIn);
        Assert.Equal(400, spend.TokensCached);
        Assert.Equal(200, spend.TokensOut);
        // 1600 входящих по 2, 400 кешированных по 0,5, 200 исходящих по 10 — за тысячу
        Assert.Equal(3.2m + 0.2m + 2m, spend.Cost);
    }

    /// <summary>Ход без токенов (ходить было некуда) строки расхода не создаёт</summary>
    [Fact]
    public async Task RunWithoutTokensWritesNothing()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        await world.Db.AiModels.ExecuteUpdateAsync(x => x.SetProperty(m => m.IsEnabled, false), CancellationToken.None);
        var run = world.StartRun();
        await world.Save();

        // Act
        await world.Runner(new FakeChatClient()).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantRunStatus.Failed, run.Status);
        Assert.False(await world.NewContext().RunSpend.AnyAsync(CancellationToken.None));
    }

    /// <summary>
    /// Ход учитывается один раз: повторный учёт того же прогона ничего не добавляет
    /// </summary>
    [Fact]
    public async Task SpendIsRecordedOnce()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        var run = world.StartRun();
        run.AiModelId = world.Model.Id;
        run.TokensIn = 10;
        await world.Save();
        var billing = new RunBilling(world.Db, world.BillingMetrics,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RunBilling>.Instance);
        await billing.Charge(run, CancellationToken.None);
        await world.Save();

        // Act
        await billing.Charge(run, CancellationToken.None);
        await world.Save();

        // Assert
        Assert.Equal(1, await world.NewContext().RunSpend.CountAsync(CancellationToken.None));
    }

    /// <summary>
    /// Компакция — служебный расход: своя строка по модели, которая ответила,
    /// с пространством треда, и в расход ходов она не попадает
    /// </summary>
    [Fact]
    public async Task CompactionWritesServiceSpend()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        await Price(world, costIn: 1m, costCached: 1m, costOut: 1m);
        world.Settings.HistoryMessages = 2;
        for (var i = 0; i < 6; i++)
            world.Message(i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant, $"реплика {i}");
        await world.Save();
        var chat = new FakeChatClient().Says("Говорили про горы.").Costing(3000, 1000);

        // Act
        await world.Compaction(chat).Handle(
            new AssistantCompactionCommand { UserId = world.Author, WorkspaceId = world.WorkspaceId },
            CancellationToken.None);

        // Assert
        var db = world.NewContext();
        var spend = await db.ServiceSpend.SingleAsync(CancellationToken.None);
        Assert.Equal(ServiceSpendKind.Compaction, spend.Kind);
        Assert.Equal(world.Model.Id, spend.ModelId);
        Assert.Equal(world.WorkspaceId, spend.WorkspaceId);
        Assert.Equal(4m, spend.Cost);
        Assert.False(await db.RunSpend.AnyAsync(CancellationToken.None));
    }

    /// <summary>Прайс модели мира — в базе: ход читает справочник оттуда</summary>
    internal static Task Price(AssistantWorld world, decimal costIn, decimal costCached, decimal costOut) =>
        world.Db.AiModels
            .Where(x => x.Id == world.Model.Id)
            .ExecuteUpdateAsync(x => x
                .SetProperty(m => m.CostIn, costIn)
                .SetProperty(m => m.CostCached, costCached)
                .SetProperty(m => m.CostOut, costOut), CancellationToken.None);
}
