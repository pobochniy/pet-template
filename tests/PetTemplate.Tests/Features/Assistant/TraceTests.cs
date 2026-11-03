using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Трасса прогона и «Показать ход мысли» (спец. assistant, 13.1–13.2): каждый шаг —
/// обращение к модели и вызов инструмента — пишется, упавший тоже; человек видит свой
/// ход строками, а «Очистить» и срок хранения трассу убирают, не трогая прогон
/// </summary>
public class TraceTests
{
    /// <summary>Шаг инструмента и обращения к модели — в трассе, по порядку, с токенами</summary>
    [Fact]
    public async Task RunStepsAreTraced()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient()
            .Calling("search_help", new { query = "как пригласить" }).Costing(100, 10)
            .Says("По справке — так.").Costing(150, 20);

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var steps = await world.NewContext().AssistantTraceSteps.OfRun(run.Id).ToListAsync(CancellationToken.None);
        Assert.Equal(["model", "tool", "model"], steps.Select(x => x.Kind));
        Assert.Equal("search_help", steps[1].ToolName);
        Assert.Equal("как пригласить", JsonDocument.Parse(steps[1].Input).RootElement.GetProperty("query").GetString());
        // Кириллица — как есть: аргументы читают в «Диалогах» админки (спец. admin, 16.2)
        Assert.Contains("как пригласить", steps[1].Input);
        Assert.Equal(100, steps[0].TokensIn);
        Assert.Equal(20, steps[2].TokensOut);
        Assert.All(steps, x => Assert.Null(x.Error));
    }

    /// <summary>
    /// Модель упала — шаг всё равно в трассе, с ошибкой: трасса нужна прежде всего
    /// упавшему ходу, который до общего сохранения не доживает
    /// </summary>
    [Fact]
    public async Task FailedModelStepIsTraced()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient { OnAsk = _ => throw new HttpRequestException("поставщик молчит") };

        // Act
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            world.Runner(chat).Execute(run.Id, null, CancellationToken.None));

        // Assert
        var step = await world.NewContext().AssistantTraceSteps.SingleAsync(CancellationToken.None);
        Assert.Equal("model", step.Kind);
        Assert.Equal("поставщик молчит", step.Error);
    }

    /// <summary>Кусок трассы длиннее 4 КБ усекается с пометкой — больше человек не прочтёт</summary>
    [Fact]
    public async Task LongStepIsTrimmed()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient().Says(new string('я', 10_000));

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var step = await world.NewContext().AssistantTraceSteps.SingleAsync(CancellationToken.None);
        Assert.EndsWith("…[усечено]", step.Output);
        Assert.True(step.Output!.Length < 5000);
    }

    /// <summary>«Показать ход мысли»: шаги человеческой строкой, без JSON аргументов</summary>
    [Fact]
    public async Task OwnTraceReadsAsSentences()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        var run = world.StartRun();
        await world.Save();
        await world.Runner(new FakeChatClient()
                .Calling("search_help", new { query = "как пригласить" })
                .Says("Так."))
            .Execute(run.Id, null, CancellationToken.None);

        // Act
        var response = await world.Client.GetAsync($"/api/Assistant/Run/{run.Id}/Trace", CancellationToken.None);

        // Assert
        response.EnsureSuccessStatusCode();
        var trace = await response.Content.ReadFromJsonAsync<AssistantTraceDto>(CancellationToken.None);
        Assert.False(trace!.Expired);
        Assert.Equal(["обращение к модели", "искал в справке: как пригласить", "обращение к модели"],
            trace.Steps.Select(x => x.Summary));
    }

    /// <summary>
    /// Запись в память — тоже шаг, который человек должен узнать в трассе: глаголом,
    /// а не именем инструмента (13.2)
    /// </summary>
    [Fact]
    public void RememberFactReadsAsVerb()
    {
        // Act
        var summary = new TraceSummary([]).Build("tool", "remember_fact",
            """{"key":"кофе","text":"пьёт кофе без сахара","kind":"preference","personal":true}""");

        // Assert
        Assert.Equal("запомнил: пьёт кофе без сахара", summary);
    }

    /// <summary>Чужой ход — <c>404</c>, как несуществующий (раздел 14)</summary>
    [Fact]
    public async Task ForeignTraceIsNotFound()
    {
        // Arrange
        var world = await AssistantWorld.Build("Котики и Собачки");
        var vasya = new UserBuilder("Vasya").WithAllPermissions(world.WorkspaceId).Please().profile;
        world.Db.Profiles.Add(vasya);
        var run = world.StartRun();
        run.UserId = vasya.Id;
        await world.Save();

        // Act
        var response = await world.Client.GetAsync($"/api/Assistant/Run/{run.Id}/Trace", CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Трассу вычистили по сроку — флаг «подробности уже не хранятся», а не пустой список
    /// и не ошибка; прогон при этом остаётся (12.3)
    /// </summary>
    [Fact]
    public async Task OldTraceIsCleanedAndReportedExpired()
    {
        // Arrange
        var world = await AssistantWorld.Build("Винтики и Шпунтики");
        var old = world.Now.AddDays(-world.Settings.TraceRetentionDays - 1);
        var run = world.StartRun();
        run.StartedAtUtc = old;
        world.Db.AssistantTraceSteps.Add(Step(run.Id, old));
        await world.Save();

        // Act
        var deleted = await new TraceWriter(world.Db, Options.Create(world.Settings)).CleanupOld();
        var trace = await world.Threads.Trace(run.Id, world.Author, world.WorkspaceId, CancellationToken.None);

        // Assert
        Assert.Equal(1, deleted);
        Assert.True(trace!.Expired);
        Assert.True(await world.NewContext().AssistantRuns.AnyAsync(x => x.Id == run.Id, CancellationToken.None));
    }

    /// <summary>«Очистить» уносит трассу вместе с перепиской, прогон остаётся (12.1.1)</summary>
    [Fact]
    public async Task ClearDeletesTrace()
    {
        // Arrange
        var world = await AssistantWorld.Build("Крылышки и Ножки");
        var run = world.StartRun();
        run.Status = AssistantRunStatus.Done;
        world.Db.AssistantTraceSteps.Add(Step(run.Id, world.Now));
        await world.Save();

        // Act
        var response = await world.Client.DeleteAsync("/api/Assistant/Clear", CancellationToken.None);

        // Assert
        response.EnsureSuccessStatusCode();
        var db = world.NewContext();
        Assert.False(await db.AssistantTraceSteps.AnyAsync(CancellationToken.None));
        Assert.True(await db.AssistantRuns.AnyAsync(x => x.Id == run.Id, CancellationToken.None));
    }

    private static AssistantTraceStep Step(Guid runId, DateTime at) => new()
    {
        RunId = runId,
        Step = 0,
        Kind = "model",
        Input = "как пригласить",
        CreatedAtUtc = at
    };

    /// <summary>
    /// Глагол инструмента раздела приносит раздел (7.1, 13.2), глаголы ядра он не переписывает
    /// </summary>
    [Fact]
    public void SectionVerbsExtendTrace()
    {
        // Arrange
        var summary = new TraceSummary([new PetTemplate.Tests.Features.Assistant.TestPlan.TestNoteSection(new())]);

        // Act
        var line = summary.Build("tool", "find_test_notes", "{}");

        // Assert
        Assert.Equal(PetTemplate.Tests.Features.Assistant.TestPlan.TestNoteSection.FindVerb, line);
    }
}
