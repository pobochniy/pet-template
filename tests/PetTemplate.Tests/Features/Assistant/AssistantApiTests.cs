using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Services.Commands;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Features.Assistant.TestPlan;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// API ассистента (спец. assistant, раздел 14): лента, отправка, долгий опрос хода,
/// «Остановить», «Очистить» и права — любой участник пространства, только свой тред
/// </summary>
public class AssistantApiTests
{
    /// <summary>Отправка отвечает ходом всегда — и ставит его командой в очередь (раздел 3)</summary>
    [Fact]
    public async Task SendAnswersWithRun()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        world.Client.DefaultRequestHeaders.Add("X-Time-Zone", "Asia/Almaty");

        // Act
        var response = await world.Client.PostAsJsonAsync("/api/Assistant/Send",
            new AssistantSendDto { Text = "как пригласить коллегу?" }, CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AssistantSendResultDto>(CancellationToken.None);
        var run = await world.Db.AssistantRuns.SingleAsync(CancellationToken.None);
        Assert.Equal(result!.RunId, run.Id);
        Assert.Equal(AssistantRunStatus.Running, run.Status);
        Assert.Equal("Asia/Almaty", run.TimeZone);
        Assert.Single(await QueueAssert.Commands<AssistantRunCommand>(world.Db));
    }

    /// <summary>Неизвестная зона — UTC, а не отказ запроса (раздел 10)</summary>
    [Fact]
    public async Task UnknownTimeZoneIsUtc()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пряники и Вафли");
        world.Client.DefaultRequestHeaders.Add("X-Time-Zone", "Марс/Олимп");

        // Act
        var response = await world.Client.PostAsJsonAsync("/api/Assistant/Send",
            new AssistantSendDto { Text = "привет" }, CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        var run = await world.Db.AssistantRuns.SingleAsync(CancellationToken.None);
        Assert.Equal("UTC", run.TimeZone);
    }

    /// <summary>Пустая реплика — <c>422</c> у поля, а не ход с пустотой</summary>
    [Fact]
    public async Task EmptyMessageIsValidationError()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ватрушки и Пышки");

        // Act
        var response = await world.Client.PostAsJsonAsync("/api/Assistant/Send",
            new AssistantSendDto { Text = string.Empty }, CancellationToken.None);

        // Assert
        await response.ShouldBeValidation(["text"]);
        Assert.False(await world.Db.AssistantRuns.AnyAsync(CancellationToken.None));
    }

    /// <summary>
    /// Ассистент — внутри пространства: посторонний получает <c>403</c>, как у любого
    /// эндпоинта под <c>[AuthorizePermissions]</c> (спец. authorization, 5)
    /// </summary>
    [Fact]
    public async Task OutsiderIsForbidden()
    {
        // Arrange
        var (workspace, _, oleg) = Given.Owner("Ёлки и Палки");
        var ownWorkspace = new WorkspaceBuilder("Рога и Копыта").Please();
        var (token, vasya) = new UserBuilder("Vasya").WithAllPermissions(ownWorkspace.Id).Please();
        var (client, db) = await Given.ApiClient(x =>
        {
            x.Workspaces.Add(workspace);
            x.Workspaces.Add(ownWorkspace);
            x.Profiles.Add(oleg);
            x.Profiles.Add(vasya);
        }, token, workspace.Id);

        // Act
        var response = await client.PostAsJsonAsync("/api/Assistant/Send",
            new AssistantSendDto { Text = "покажи чужое" }, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await db.AssistantRuns.AnyAsync(CancellationToken.None));
    }

    /// <summary>
    /// Своего права у ассистента нет: участник с одним правом на чтение состава —
    /// такой же собеседник, как владелец (раздел 9)
    /// </summary>
    [Fact]
    public async Task AnyMemberCanTalk()
    {
        // Arrange
        var world = await AssistantWorld.Build("Тапки и Носки", PermissionEnum.WorkspaceMemberRead);

        // Act
        var response = await world.Client.PostAsJsonAsync("/api/Assistant/Send",
            new AssistantSendDto { Text = "привет" }, CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
    }

    /// <summary>Без пространства в заголовке — <c>400</c>: тред — пара «человек + пространство»</summary>
    [Fact]
    public async Task MissingWorkspaceHeaderIsBadRequest()
    {
        // Arrange
        var world = await AssistantWorld.Build("Гвозди и Шурупы");
        world.Client.DefaultRequestHeaders.Remove("X-Workspace-Id");

        // Act
        var response = await world.Client.GetAsync("/api/Assistant/History", CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Чужой ход — как несуществующий: «нет» и «не твоё» не различаются</summary>
    [Fact]
    public async Task StrangersRunIsNotFound()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        var (_, stranger) = new UserBuilder("Petya").WithAllPermissions(world.WorkspaceId).Please();
        await world.Db.Profiles.AddAsync(stranger, CancellationToken.None);
        var run = world.StartRun();
        run.UserId = stranger.Id;
        await world.Save();

        // Act
        var response = await world.Client.GetAsync($"/api/Assistant/Run/{run.Id}", CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Законченный ход отвечает сразу, даже с <c>wait</c> — долгий опрос ждать нечего</summary>
    [Fact]
    public async Task FinishedRunAnswersImmediately()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        var run = world.StartRun();
        run.Status = AssistantRunStatus.Done;
        run.StopReason = AssistantStopReason.Completed;
        run.FinishedAtUtc = world.Now;
        await world.Save();
        var started = DateTime.UtcNow;

        // Act
        var response = await world.Client.GetAsync($"/api/Assistant/Run/{run.Id}?wait=20", CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        var dto = await response.Content.ReadFromJsonAsync<AssistantRunDto>(CancellationToken.None);
        Assert.Equal(AssistantRunStatus.Done, dto!.Status);
    }

    /// <summary>
    /// Долгий опрос отвечает в момент изменения, а не по истечении секунд: шаг хода
    /// будит ожидающего (раздел 14)
    /// </summary>
    [Fact]
    public async Task LongPollWakesUpOnStep()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        var run = world.StartRun();
        await world.Save();
        var started = DateTime.UtcNow;
        var polling = world.Client.GetAsync($"/api/Assistant/Run/{run.Id}?wait=20", CancellationToken.None);
        await Task.Delay(300, CancellationToken.None);

        // Act
        await using (var other = world.NewContext())
        {
            await other.AssistantRuns.Where(x => x.Id == run.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.Step, 1), CancellationToken.None);
        }

        var response = await polling;

        // Assert
        await response.ShouldBeSuccessful();
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        var dto = await response.Content.ReadFromJsonAsync<AssistantRunDto>(CancellationToken.None);
        Assert.Equal(1, dto!.Step);
    }

    /// <summary>
    /// Лента везёт план разобранным документом: блок плана клиент рисует из него,
    /// а не из прозы модели (5.1)
    /// </summary>
    [Fact]
    public async Task HistoryCarriesPlanOfTheMessage()
    {
        // Arrange
        var world = await AssistantWorld.Build("Крылышки и Ножки");
        var plan = world.Plan(AssistantWorld.CreateNote("Купить билеты"));
        world.Message(AssistantMessageRole.User, "запиши про билеты");
        world.Message(AssistantMessageRole.Assistant, "Вот что предлагаю", plan.Id, "[\"да\",\"нет\"]");
        await world.Save();

        // Act
        var response = await world.Client.GetAsync("/api/Assistant/History", CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        var history = await response.Content.ReadFromJsonAsync<AssistantHistoryDto>(CancellationToken.None);
        Assert.Equal(2, history!.Messages.Count);
        var withPlan = history.Messages.Single(x => x.Plan != null);
        Assert.Equal(plan.Id, withPlan.Plan!.Id);
        Assert.Equal(AssistantPlanStatus.Pending, withPlan.Plan.Status);
        Assert.Equal(["да", "нет"], withPlan.Options!);
        var operation = Assert.Single(withPlan.Plan.Operations.Operations);
        Assert.Equal(TestNoteKinds.CreateNote, operation.Kind);
        Assert.Null(history.ActiveRun);
    }

    /// <summary>Идущий ход приезжает с лентой — клиент рисует плашку сразу при входе</summary>
    [Fact]
    public async Task HistoryCarriesActiveRun()
    {
        // Arrange
        var world = await AssistantWorld.Build("Вилки и Ножи");
        var run = world.StartRun();
        await world.Save();

        // Act
        var response = await world.Client.GetAsync("/api/Assistant/History", CancellationToken.None);

        // Assert
        var history = await response.Content.ReadFromJsonAsync<AssistantHistoryDto>(CancellationToken.None);
        Assert.Equal(run.Id, history!.ActiveRun!.Id);
    }

    /// <summary>
    /// «Очистить» стирает переписку, а прогоны и планы остаются; незакрытый план
    /// закрывается — сообщения с ним больше нет (12.1.1)
    /// </summary>
    [Fact]
    public async Task ClearKeepsRunsAndPlans()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ложки и Поварёшки");
        var plan = world.Plan(AssistantWorld.CreateNote("Купить билеты"));
        var run = world.StartRun();
        run.Status = AssistantRunStatus.Done;
        world.Message(AssistantMessageRole.User, "запиши про билеты");
        world.Message(AssistantMessageRole.Assistant, "Делаем?", plan.Id);
        await world.Save();

        // Act
        var response = await world.Client.DeleteAsync("/api/Assistant/Clear", CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        Assert.False(await world.Db.AssistantMessages.AnyAsync(CancellationToken.None));
        Assert.True(await world.Db.AssistantRuns.AnyAsync(CancellationToken.None));
        var stored = await world.Db.AssistantPlans.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Cancelled, stored.Status);
    }

    /// <summary>«Остановить» свой ход — статус в базе, гарантия для любого экземпляра API</summary>
    [Fact]
    public async Task StopMarksRunStopped()
    {
        // Arrange
        var world = await AssistantWorld.Build("Чашки и Блюдца");
        var run = world.StartRun();
        await world.Save();

        // Act
        var response = await world.Client.PostAsync($"/api/Assistant/Run/{run.Id}/Stop", null, CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        var stored = await world.Db.AssistantRuns.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantRunStatus.Stopped, stored.Status);
    }

    /// <summary>«Остановить» чужой ход — <c>404</c>, как несуществующий</summary>
    [Fact]
    public async Task StopOfStrangersRunIsNotFound()
    {
        // Arrange
        var world = await AssistantWorld.Build("Котики и Собачки");

        // Act
        var response = await world.Client.PostAsync($"/api/Assistant/Run/{Guid.CreateVersion7()}/Stop", null,
            CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
