using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Admin;
using PetTemplate.Services.Assistant;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Разбор диалогов и журнал доступа (спец. admin, 16.2): трасса открывается только
/// с ролью <c>Dialogs</c>, каждое открытие оставляет запись, а запись читает
/// <c>AdminManagement</c>. Прогон старше трассы остаётся в списке (спец. assistant, 12.3)
/// </summary>
public class AdminDialogsApiTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly PetTemplate.Db.Entity.Profile _oleg = new UserBuilder("Oleg").Please().profile;

    private readonly PetTemplate.Db.Entity.Profile _vasya = new UserBuilder("Vasya").Please().profile;

    private readonly PetTemplate.Db.Entity.Workspace _horns = new WorkspaceBuilder("Рога и Копыта").Please();

    private readonly PetTemplate.Db.Entity.Workspace _balls = new WorkspaceBuilder("Шарики и Палочки").Please();

    private readonly AiModel _seed = AssistantWorld.AiModel("Основная", 1, Now);

    private readonly AiModel _yandex = AssistantWorld.AiModel("YandexGPT", 2, Now);

    [Fact]
    public async Task OpeningDialogWritesAccessLog()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (client, db) = await Client([AdminRole.Dialogs], x =>
        {
            x.AssistantRuns.Add(run);
            x.AssistantTraceSteps.Add(Step(run, 1, "tool", "search_help", """{"query":"пригласить"}""", "[]"));
        });

        // Act
        var response = await client.GetAsync($"/api/Admin/Dialogs/{run.Id}");

        // Assert
        await response.ShouldBeSuccessful();
        var trace = (await response.Content.ReadFromJsonAsync<AdminDialogTraceDto>())!;
        Assert.False(trace.Expired);
        Assert.Null(trace.Evaluation);
        Assert.Equal("Рога и Копыта", trace.Dialog.WorkspaceName);
        var step = Assert.Single(trace.Steps);
        Assert.Equal(("""{"query":"пригласить"}""", "[]"), (step.Input, step.Output));
        Assert.Equal("искал в справке: пригласить", step.Summary);

        var entry = await db.AdminAccessLog.AsNoTracking().SingleAsync();
        Assert.Equal((_oleg.Id, run.Id, _horns.Id), (entry.AdminUserId!.Value, entry.RunId, entry.WorkspaceId));
    }

    /// <summary>Два открытия — две записи: журнал считает чтения, а не прогоны</summary>
    [Fact]
    public async Task EachOpeningIsLogged()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (client, db) = await Client([AdminRole.Dialogs], x => x.AssistantRuns.Add(run));

        // Act
        await (await client.GetAsync($"/api/Admin/Dialogs/{run.Id}")).ShouldBeSuccessful();
        await (await client.GetAsync($"/api/Admin/Dialogs/{run.Id}")).ShouldBeSuccessful();

        // Assert
        Assert.Equal(2, await db.AdminAccessLog.CountAsync());
    }

    /// <summary>Список сам по себе содержимого не показывает — и в журнал не пишется</summary>
    [Fact]
    public async Task ListingIsNotLogged()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (client, db) = await Client([AdminRole.Dialogs], x => x.AssistantRuns.Add(run));

        // Act
        var response = await client.GetAsync("/api/Admin/Dialogs");

        // Assert
        await response.ShouldBeSuccessful();
        var page = (await response.Content.ReadFromJsonAsync<AdminDialogPageDto>())!;
        Assert.Equal(run.Id, Assert.Single(page.Dialogs).RunId);
        Assert.False(await db.AdminAccessLog.AnyAsync());
    }

    /// <summary>
    /// Список — страницами по 50, новые сверху: полная страница кончается курсором
    /// на свой последний прогон, с которого клиент просит следующую
    /// </summary>
    [Fact]
    public async Task FullPageEndsWithCursor()
    {
        // Arrange
        var runs = Enumerable.Range(1, 51).Select(hoursAgo => Run(_horns, hoursAgo)).ToArray();
        var (client, _) = await Client([AdminRole.Dialogs], x => x.AssistantRuns.AddRange(runs));

        // Act
        var response = await client.GetAsync("/api/Admin/Dialogs");

        // Assert
        await response.ShouldBeSuccessful();
        var page = (await response.Content.ReadFromJsonAsync<AdminDialogPageDto>())!;
        Assert.Equal(runs.Take(50).Select(x => x.Id), page.Dialogs.Select(x => x.RunId));
        Assert.Equal(runs[49].Id, page.NextBefore);
    }

    /// <summary>Остальные роли, включая <c>AdminManagement</c>, чужой переписки не видят</summary>
    [Fact]
    public async Task DialogsNeedDialogsRole()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (client, db) = await Client(
            [AdminRole.Models, AdminRole.Billing, AdminRole.AdminManagement],
            x => x.AssistantRuns.Add(run));

        // Act
        var list = await client.GetAsync("/api/Admin/Dialogs");
        var open = await client.GetAsync($"/api/Admin/Dialogs/{run.Id}");
        var evaluate = await client.PostAsJsonAsync("/api/Admin/Dialogs/Evaluations",
            new AdminEvaluationStartDto { ModelId = _seed.Id, BatchSize = 1 });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, open.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, evaluate.StatusCode);
        Assert.False(await db.AdminAccessLog.AnyAsync());
        Assert.False(await db.AdminJobs.AnyAsync());
    }

    [Fact]
    public async Task UnknownRunIsNotFoundAndNotLogged()
    {
        // Arrange
        var (client, db) = await Client([AdminRole.Dialogs]);

        // Act
        var response = await client.GetAsync($"/api/Admin/Dialogs/{Guid.CreateVersion7()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.AdminAccessLog.AnyAsync());
    }

    /// <summary>
    /// Прогон старше трассы остаётся в списке со своей моделью и счётчиками, а трасса
    /// отвечает «содержимого уже нет» — и открытие всё равно записано
    /// </summary>
    [Fact]
    public async Task OldRunStaysInListWithoutTrace()
    {
        // Arrange
        var old = Run(_horns, hoursAgo: 40 * 24);
        old.Step = 7;
        var (client, db) = await Client([AdminRole.Dialogs], x => x.AssistantRuns.Add(old));
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)).ToString("yyyy-MM-dd");

        // Act
        var list = await client.GetAsync($"/api/Admin/Dialogs?from={from}");
        var open = await client.GetAsync($"/api/Admin/Dialogs/{old.Id}");

        // Assert
        await list.ShouldBeSuccessful();
        var row = Assert.Single((await list.Content.ReadFromJsonAsync<AdminDialogPageDto>())!.Dialogs);
        Assert.Equal((old.Id, 7, "Основная", true), (row.RunId, row.Steps, row.ModelName, row.TraceExpired));

        await open.ShouldBeSuccessful();
        var trace = (await open.Content.ReadFromJsonAsync<AdminDialogTraceDto>())!;
        Assert.True(trace.Expired);
        Assert.Empty(trace.Steps);
        Assert.Equal(1, await db.AdminAccessLog.CountAsync());
    }

    /// <summary>Свежий прогон без шагов — не «уже нет»: содержимого у него и не было</summary>
    [Fact]
    public async Task FreshRunWithoutStepsIsNotExpired()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (client, _) = await Client([AdminRole.Dialogs], x => x.AssistantRuns.Add(run));

        // Act
        var response = await client.GetAsync($"/api/Admin/Dialogs/{run.Id}");

        // Assert
        var trace = (await response.Content.ReadFromJsonAsync<AdminDialogTraceDto>())!;
        Assert.False(trace.Expired);
        Assert.Empty(trace.Steps);
    }

    /// <summary>Уточнения и оценку ставит автооценка: неоценённый прогон под такие фильтры не подходит</summary>
    [Fact]
    public async Task UnevaluatedRunDoesNotMatchEvaluationFilters()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (client, _) = await Client([AdminRole.Dialogs], x => x.AssistantRuns.Add(run));

        // Act
        var bySatisfaction = await client.GetAsync("/api/Admin/Dialogs?maxSatisfaction=3");
        var byClarifications = await client.GetAsync("/api/Admin/Dialogs?minClarifications=1");

        // Assert
        Assert.Empty((await bySatisfaction.Content.ReadFromJsonAsync<AdminDialogPageDto>())!.Dialogs);
        Assert.Empty((await byClarifications.Content.ReadFromJsonAsync<AdminDialogPageDto>())!.Dialogs);
    }

    /// <summary>Период, пространство по части названия, модель и статус сужают список вместе</summary>
    [Fact]
    public async Task ListFiltersBySigns()
    {
        // Arrange
        var wanted = Run(_horns, hoursAgo: 1, _yandex);
        var failed = Run(_horns, hoursAgo: 3, _yandex);
        failed.Status = AssistantRunStatus.Failed;
        var (client, _) = await Client([AdminRole.Dialogs], x => x.AssistantRuns.AddRange(
            wanted,
            Run(_horns, hoursAgo: 2),
            failed,
            Run(_balls, hoursAgo: 4, _yandex),
            Run(_horns, hoursAgo: 10 * 24, _yandex)));

        // Act
        var response = await client.GetAsync(
            $"/api/Admin/Dialogs?workspace=копыта&modelId={_yandex.Id}&status={(int)AssistantRunStatus.Done}");

        // Assert
        await response.ShouldBeSuccessful();
        var row = Assert.Single((await response.Content.ReadFromJsonAsync<AdminDialogPageDto>())!.Dialogs);
        Assert.Equal((wanted.Id, "YandexGPT"), (row.RunId, row.ModelName));
    }

    /// <summary>Строка: новые сверху, стоимость хода по строке расхода, ход без расхода — нулём</summary>
    [Fact]
    public async Task ListShowsCostNewestFirst()
    {
        // Arrange
        var older = Run(_horns, hoursAgo: 2);
        var (client, _) = await Client([AdminRole.Dialogs], x =>
        {
            x.AssistantRuns.AddRange(older, Run(_balls, hoursAgo: 1));
            x.RunSpend.Add(new RunSpend
            {
                Id = Guid.CreateVersion7(),
                RunId = older.Id,
                WorkspaceId = _horns.Id,
                UserId = _vasya.Id,
                ModelId = _seed.Id,
                TokensIn = 1000,
                Cost = 2.5m,
                CreatedUtc = Now
            });
        });

        // Act
        var response = await client.GetAsync("/api/Admin/Dialogs");

        // Assert
        var rows = (await response.Content.ReadFromJsonAsync<AdminDialogPageDto>())!.Dialogs;
        Assert.Equal(["Шарики и Палочки", "Рога и Копыта"], rows.Select(x => x.WorkspaceName));
        Assert.Equal([0m, 2.5m], rows.Select(x => x.Cost));
    }

    /// <summary>
    /// Оценка автооценки (16.3) сужает список и видна в строке и в трассе: «покажи худшие» —
    /// оценка не выше и уточнений не меньше заданного
    /// </summary>
    [Fact]
    public async Task EvaluationFiltersAndShowsScores()
    {
        // Arrange
        var bad = Run(_horns, hoursAgo: 1);
        var good = Run(_horns, hoursAgo: 2);
        var quiet = Run(_horns, hoursAgo: 3);
        var (client, _) = await Client([AdminRole.Dialogs], x =>
        {
            x.AssistantRuns.AddRange(bad, good, quiet, Run(_horns, hoursAgo: 4));
            x.DialogEvaluations.AddRange(
                Evaluation(bad, satisfaction: 2, clarifications: 3),
                Evaluation(good, satisfaction: 5, clarifications: 3),
                Evaluation(quiet, satisfaction: 1, clarifications: 0));
        });

        // Act
        var response = await client.GetAsync("/api/Admin/Dialogs?maxSatisfaction=3&minClarifications=2");

        // Assert
        await response.ShouldBeSuccessful();
        var row = Assert.Single((await response.Content.ReadFromJsonAsync<AdminDialogPageDto>())!.Dialogs);
        Assert.Equal((bad.Id, 2, 3), (row.RunId, row.Satisfaction!.Value, row.Clarifications!.Value));
        var trace = await client.GetFromJsonAsync<AdminDialogTraceDto>($"/api/Admin/Dialogs/{bad.Id}");
        Assert.Equal(("Основная", "Кружил вокруг справки."), (trace!.Evaluation!.ModelName, trace.Evaluation.Notes));
    }

    /// <summary>
    /// Прогоны, начатые в одну метку, на границе страницы не теряются: курсор —
    /// пара (время, Id), а не одно время
    /// </summary>
    [Fact]
    public async Task ListKeepsRunsWithSameTimeAfterCursor()
    {
        // Arrange
        var first = Run(_horns, hoursAgo: 1);
        var second = Run(_balls, hoursAgo: 1);
        second.StartedAtUtc = first.StartedAtUtc;
        // Порядок пары — как его видит Postgres: uuid сравнивается побайтно, как и строкой
        var ids = new[] { first.Id, second.Id }.OrderBy(x => x.ToString(), StringComparer.Ordinal).ToArray();
        var (client, _) = await Client([AdminRole.Dialogs], x => x.AssistantRuns.AddRange(first, second));

        // Act
        var response = await client.GetAsync($"/api/Admin/Dialogs?before={ids[1]}");

        // Assert
        await response.ShouldBeSuccessful();
        var rows = (await response.Content.ReadFromJsonAsync<AdminDialogPageDto>())!.Dialogs;
        Assert.Equal([ids[0]], rows.Select(x => x.RunId));
    }

    /// <summary>Журнал читает <c>AdminManagement</c> — кто, когда, чей прогон</summary>
    [Fact]
    public async Task AccessLogIsVisibleToAdminManagement()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (client, _) = await Client([AdminRole.AdminManagement], x =>
        {
            x.AssistantRuns.Add(run);
            x.AdminAccessLog.Add(Entry(_vasya.Id, run, hoursAgo: 1));
        });

        // Act
        var response = await client.GetAsync("/api/Admin/AccessLog");

        // Assert
        await response.ShouldBeSuccessful();
        var entry = Assert.Single((await response.Content.ReadFromJsonAsync<AdminAccessLogPageDto>())!.Entries);
        Assert.Equal(("Vasya", run.Id, "Рога и Копыта"), (entry.AdminName, entry.RunId, entry.WorkspaceName));
    }

    /// <summary>Смысл записи в том, что её смотрит не тот, кто её оставил</summary>
    [Fact]
    public async Task AccessLogIsHiddenFromDialogsRole()
    {
        // Arrange
        var (client, _) = await Client([AdminRole.Dialogs]);

        // Act
        var response = await client.GetAsync("/api/Admin/AccessLog");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AccessLogFiltersByAdminAndPeriod()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (client, _) = await Client([AdminRole.AdminManagement], x =>
        {
            x.AssistantRuns.Add(run);
            x.AdminAccessLog.AddRange(
                Entry(_vasya.Id, run, hoursAgo: 1),
                Entry(_vasya.Id, run, hoursAgo: 10 * 24),
                Entry(_oleg.Id, run, hoursAgo: 1));
        });
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)).ToString("yyyy-MM-dd");

        // Act
        var response = await client.GetAsync($"/api/Admin/AccessLog?adminUserId={_vasya.Id}&from={from}");

        // Assert
        var entry = Assert.Single((await response.Content.ReadFromJsonAsync<AdminAccessLogPageDto>())!.Entries);
        Assert.Equal(_vasya.Id, entry.AdminUserId);
        Assert.True(entry.CreatedUtc > DateTime.UtcNow.AddDays(-2));
    }

    /// <summary>
    /// Журнал живёт год — дольше трассы — и уходит только по сроку; запись пережила
    /// снос пространства — строка без названия
    /// </summary>
    [Fact]
    public async Task AccessLogCleanupRemovesOnlyExpired()
    {
        // Arrange
        var run = Run(_horns, hoursAgo: 1);
        var (_, db) = await Client([AdminRole.AdminManagement], x =>
        {
            x.AssistantRuns.Add(run);
            x.AdminAccessLog.AddRange(Entry(_vasya.Id, run, hoursAgo: 400 * 24), Entry(_vasya.Id, run, hoursAgo: 40 * 24));
        });
        var service = new AdminAccessLogService(db, Microsoft.Extensions.Options.Options.Create(new PetTemplate.Shared.Options.AdminOptions()));

        // Act
        var deleted = await service.CleanupOld();

        // Assert
        Assert.Equal(1, deleted);
        Assert.True((await db.AdminAccessLog.AsNoTracking().SingleAsync()).CreatedUtc > DateTime.UtcNow.AddDays(-41));
    }

    /// <summary>Оценка живёт год — дольше трассы — и уходит только по сроку (16.3)</summary>
    [Fact]
    public async Task EvaluationCleanupRemovesOnlyExpired()
    {
        // Arrange
        var old = Evaluation(Run(_horns, hoursAgo: 400 * 24), satisfaction: 3, clarifications: 0);
        old.CreatedUtc = DateTime.UtcNow.AddDays(-400);
        var recent = Evaluation(Run(_horns, hoursAgo: 40 * 24), satisfaction: 3, clarifications: 0);
        recent.CreatedUtc = DateTime.UtcNow.AddDays(-40);
        var services = await Given.Services(x =>
        {
            Arrange(x, [AdminRole.Dialogs]);
            x.DialogEvaluations.AddRange(old, recent);
        });
        using var scope = services.CreateScope();

        // Act
        var deleted = await scope.ServiceProvider.GetRequiredService<DialogEvaluationService>().CleanupOld();

        // Assert
        Assert.Equal(1, deleted);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.Equal(recent.Id, (await db.DialogEvaluations.AsNoTracking().SingleAsync()).Id);
    }

    /// <summary>
    /// Чистка трасс уносит шаги, но не прогон: он остаётся в списке и отвечает
    /// «содержимого уже нет»
    /// </summary>
    [Fact]
    public async Task TraceCleanupLeavesRunInList()
    {
        // Arrange
        var old = Run(_horns, hoursAgo: 40 * 24);
        var (client, db) = await Client([AdminRole.Dialogs], x =>
        {
            x.AssistantRuns.Add(old);
            x.AssistantTraceSteps.Add(Step(old, 1, "model", null, "…", null));
        });
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)).ToString("yyyy-MM-dd");

        // Act
        await new TraceWriter(db, Microsoft.Extensions.Options.Options.Create(new PetTemplate.Shared.Options.AssistantOptions()))
            .CleanupOld();
        var list = await client.GetFromJsonAsync<AdminDialogPageDto>($"/api/Admin/Dialogs?from={from}");
        var trace = await client.GetFromJsonAsync<AdminDialogTraceDto>($"/api/Admin/Dialogs/{old.Id}");

        // Assert
        Assert.True(Assert.Single(list!.Dialogs).TraceExpired);
        Assert.True(trace!.Expired);
        Assert.Empty(trace.Steps);
    }

    private AssistantRun Run(PetTemplate.Db.Entity.Workspace workspace, int hoursAgo, AiModel? model = null)
    {
        var started = DateTime.UtcNow.AddHours(-hoursAgo);
        return new AssistantRun
        {
            Id = Guid.CreateVersion7(started),
            UserId = _vasya.Id,
            WorkspaceId = workspace.Id,
            TimeZone = "Europe/Moscow",
            Language = "ru",
            Status = AssistantRunStatus.Done,
            StopReason = AssistantStopReason.Completed,
            Step = 1,
            StartedAtUtc = started,
            FinishedAtUtc = started.AddMinutes(1),
            AiModelId = (model ?? _seed).Id
        };
    }

    private static AssistantTraceStep Step(AssistantRun run, int step, string kind, string? tool, string input, string? output) => new()
    {
        RunId = run.Id,
        Step = step,
        Kind = kind,
        ToolName = tool,
        Input = input,
        Output = output,
        DurationMs = 12,
        CreatedAtUtc = run.StartedAtUtc
    };

    private DialogEvaluation Evaluation(AssistantRun run, int satisfaction, int clarifications) => new()
    {
        Id = Guid.CreateVersion7(),
        RunId = run.Id,
        ModelId = _seed.Id,
        Satisfaction = satisfaction,
        Clarifications = clarifications,
        Notes = "Кружил вокруг справки.",
        CreatedUtc = DateTime.UtcNow
    };

    private static AdminAccessLog Entry(Guid adminId, AssistantRun run, int hoursAgo) => new()
    {
        Id = Guid.CreateVersion7(),
        AdminUserId = adminId,
        RunId = run.Id,
        WorkspaceId = run.WorkspaceId,
        CreatedUtc = DateTime.UtcNow.AddHours(-hoursAgo)
    };

    private void Arrange(ApplicationContext db, AdminRole[] roles)
    {
        db.Profiles.AddRange(_oleg, _vasya);
        db.Workspaces.AddRange(_horns, _balls);
        db.AiModels.AddRange(_seed, _yandex);
        db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, roles));
    }

    private async Task<(HttpClient client, ApplicationContext db)> Client(
        AdminRole[] roles,
        Action<ApplicationContext>? arrange = null) =>
        await Given.AdminClient(_oleg.Id, roles, db =>
        {
            Arrange(db, roles);
            arrange?.Invoke(db);
        });
}
