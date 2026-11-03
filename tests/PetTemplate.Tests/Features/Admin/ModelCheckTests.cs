using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Admin;
using PetTemplate.Services.Assistant.Check;
using PetTemplate.Services.Assistant.Sections;
using PetTemplate.Services.Commands;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Tests.Features.Assistant.TestPlan;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Сверка модели из админки (спец. admin, 15.3): набор фраз гоняется настоящим ходом
/// в изолированном мире, итог — в отчёте запуска, токены и цена — в служебных расходах,
/// а в боевой базе не остаётся ничего, кроме запуска и расходов.
///
/// Модель — записанная (<see cref="FakeChatClient"/>): без ответов по сценарию она на
/// каждую фразу отвечает текстом, и ход кончается за одно обращение
/// </summary>
public class ModelCheckTests
{
    private readonly PetTemplate.Db.Entity.Profile _oleg = new UserBuilder("Oleg").Please().profile;

    private readonly AiModel _seed = AssistantWorld.AiModel("Основная", 1, DateTime.UtcNow);

    private static int Phrases => ModelCheckPhrase.Embedded().Count;

    /// <summary>
    /// Набор фраз — ресурс сборки: у каждой фразы есть текст, ожидаемые инструменты — из
    /// каталога ядра. Согласия на план в наборе нет: операций плана в ядре нет, и
    /// <c>apply_plan</c> модели не показывается (спец. assistant, 7)
    /// </summary>
    [Fact]
    public void PhraseSetIsEmbedded()
    {
        // Act
        var phrases = ModelCheckPhrase.Embedded();

        // Assert
        Assert.True(phrases.Count >= 15);
        Assert.All(phrases, x => Assert.False(string.IsNullOrWhiteSpace(x.Phrase)));
        Assert.All(phrases.Where(x => x.Tool != null),
            x => Assert.Contains(x.Tool, new[] { "search_help", "remember_fact", "ask_user" }));
    }

    /// <summary>
    /// «Сверить» заводит запуск на весь набор и ставит одну команду; сверить можно и
    /// выключенную модель — кандидата проверяют до включения
    /// </summary>
    [Fact]
    public async Task CanStartCheckOfDisabledModel()
    {
        // Arrange
        _seed.IsEnabled = false;
        var db = await Db();

        // Act
        var jobId = await Service(db, new FakeChatClient()).Start(_seed.Id, _oleg.Id);

        // Assert
        var job = await db.AdminJobs.AsNoTracking().SingleAsync();
        Assert.Equal(jobId, job.Id);
        Assert.Equal((AdminJobKind.ModelCheck, AdminJobStatus.Running, Phrases), (job.Kind, job.Status, job.Planned));
        Assert.Equal(jobId, Assert.Single(await QueueAssert.Commands<ModelCheckCommand>(db)).JobId);
    }

    /// <summary>Одна сверка за раз — вторую при идущей не завести</summary>
    [Fact]
    public async Task CannotStartSecondCheck()
    {
        // Arrange
        var db = await Db();
        var service = Service(db, new FakeChatClient());
        await service.Start(_seed.Id, _oleg.Id);

        // Act
        var refused = await Record.ExceptionAsync(() => service.Start(_seed.Id, _oleg.Id));

        // Assert
        Assert.IsType<AdminRejectionException>(refused);
        Assert.Equal(1, await db.AdminJobs.CountAsync());
    }

    /// <summary>
    /// Сверка проходит весь набор: в отчёте строка на каждую фразу и доли по признакам,
    /// запуск закрыт
    /// </summary>
    [Fact]
    public async Task CheckFillsReport()
    {
        // Arrange
        var db = await Db();
        var service = Service(db, new FakeChatClient());
        var jobId = await service.Start(_seed.Id, _oleg.Id);

        // Act
        await service.Run(jobId, null);

        // Assert
        var job = await db.AdminJobs.AsNoTracking().SingleAsync();
        Assert.Equal((AdminJobStatus.Done, Phrases), (job.Status, job.Done));
        var report = Report(job);
        Assert.Equal(Phrases, report.Results.Length);
        Assert.Equal(ModelCheckReport.Keys, report.Shares.Select(x => x.Key));
        Assert.Equal(Phrases, report.Shares.Single(x => x.Key == "noCalls").Hits);
        Assert.Equal(Phrases, report.Shares.Single(x => x.Key == "russian").Hits);
    }

    /// <summary>
    /// Инструмент, который фраза ждёт, засчитывается попаданием: сверка гоняет тот же
    /// цикл и те же инструменты, что в бою, — <c>search_help</c> отвечает «справка
    /// недоступна», а не падает
    /// </summary>
    [Fact]
    public async Task ToolCallIsCountedAsHit()
    {
        // Arrange
        var db = await Db();
        var first = ModelCheckPhrase.Embedded()[0];
        var chat = new FakeChatClient()
            .Calling(first.Tool!, new { query = "пригласить коллегу" })
            .Says("В справке этого нет.");
        var service = Service(db, chat);
        var jobId = await service.Start(_seed.Id, _oleg.Id);

        // Act
        await service.Run(jobId, null);

        // Assert
        var result = Report(await db.AdminJobs.AsNoTracking().SingleAsync()).Results[0];
        Assert.Equal(first.Phrase, result.Phrase);
        Assert.Contains(first.Tool!, result.Tools);
        Assert.True(result.ToolHit);
        Assert.Equal(1, result.Steps);
    }

    /// <summary>
    /// Слова об устройстве приложения в ответе — промах «без лишнего» (спец. help, 4);
    /// у фраз без запретных слов признак не считается вовсе
    /// </summary>
    [Fact]
    public async Task ForbiddenWordIsCountedAsMiss()
    {
        // Arrange
        var db = await Db();
        var phrases = ModelCheckPhrase.Embedded();
        var index = phrases.ToList().FindIndex(x => x.Forbidden != null);
        var chat = new FakeChatClient();
        for (var i = 0; i < index; i++) chat.Says("Готово.");
        chat.Says("Я работаю на " + phrases[index].Forbidden![0] + ".");
        var service = Service(db, chat);
        var jobId = await service.Start(_seed.Id, _oleg.Id);

        // Act
        await service.Run(jobId, null);

        // Assert
        var report = Report(await db.AdminJobs.AsNoTracking().SingleAsync());
        Assert.False(report.Results[index].CleanAnswer);
        var clean = report.Shares.Single(x => x.Key == "clean");
        Assert.Equal((0, phrases.Count(x => x.Forbidden != null)), (clean.Hits, clean.Considered));
    }

    /// <summary>
    /// Токены и цена фразы — строкой служебных расходов по цене сверяемой модели и
    /// формуле хода; итог — в запуске
    /// </summary>
    [Fact]
    public async Task CheckSpendIsRecorded()
    {
        // Arrange
        _seed.CostIn = 1m;
        _seed.CostOut = 1m;
        var db = await Db();
        var service = Service(db, new FakeChatClient().Says("Готово.").Costing(1000, 200));
        var jobId = await service.Start(_seed.Id, _oleg.Id);

        // Act
        await service.Run(jobId, null);

        // Assert
        var spends = await db.ServiceSpend.AsNoTracking().ToListAsync();
        Assert.Equal(Phrases, spends.Count);
        Assert.All(spends, x => Assert.Equal((ServiceSpendKind.ModelCheck, (Guid?)_seed.Id), (x.Kind, x.ModelId)));
        Assert.Equal(1.2m, spends.Sum(x => x.Cost));
        var job = await db.AdminJobs.AsNoTracking().SingleAsync();
        Assert.Equal((1000L, 200L, 1.2m), (job.TokensIn, job.TokensOut, job.Cost));
    }

    /// <summary>
    /// В боевую базу из мира сверки не уходит ни строчки, кроме запуска и расходов:
    /// ни прогонов, ни сообщений, ни памяти, ни выдуманных людей и пространств
    /// </summary>
    [Fact]
    public async Task CheckWritesNothingButJobAndSpend()
    {
        // Arrange
        var db = await Db();
        var remember = ModelCheckPhrase.Embedded().ToList().FindIndex(x => x.Tool == "remember_fact");
        var chat = new FakeChatClient();
        for (var i = 0; i < remember; i++) chat.Says("Готово.");
        chat.Calling("remember_fact", new { key = "илья", text = "Илья — это Илья Петров", kind = "Alias", personal = false })
            .Says("Запомнил.");
        var service = Service(db, chat);
        var jobId = await service.Start(_seed.Id, _oleg.Id);

        // Act
        await service.Run(jobId, null);

        // Assert
        Assert.Contains("remember_fact", Report(await db.AdminJobs.AsNoTracking().SingleAsync()).Results[remember].Tools);
        Assert.False(await db.AssistantRuns.AnyAsync());
        Assert.False(await db.AssistantMessages.AnyAsync());
        Assert.False(await db.AssistantPlans.AnyAsync());
        Assert.False(await db.AssistantFacts.AnyAsync());
        Assert.False(await db.AssistantTraceSteps.AnyAsync());
        Assert.False(await db.RunSpend.AnyAsync());
        Assert.False(await db.Workspaces.AnyAsync());
        Assert.Equal(1, await db.Profiles.CountAsync());
    }

    /// <summary>
    /// Размыкатель боевой строки сверка не трогает: отказы кандидата остаются в мире
    /// сверки, иначе проба выключала бы модель ходам людей. Сорвавшиеся фразы — в отчёте
    /// </summary>
    [Fact]
    public async Task FailingModelDoesNotOpenRealBreaker()
    {
        // Arrange
        var db = await Db();
        var chat = new FakeChatClient { OnAsk = _ => throw new HttpRequestException("поставщик лёг") };
        var service = Service(db, chat);
        var jobId = await service.Start(_seed.Id, _oleg.Id);

        // Act
        await service.Run(jobId, null);

        // Assert
        var seed = await db.AiModels.AsNoTracking().SingleAsync();
        Assert.Equal((0, (DateTime?)null), (seed.FailureCount, seed.OpenUntilUtc));
        var job = await db.AdminJobs.AsNoTracking().SingleAsync();
        Assert.Equal(AdminJobStatus.Done, job.Status);
        Assert.Equal(Phrases, Report(job).Shares.Single(x => x.Key == "failed").Hits);
        Assert.False(await db.ServiceSpend.AnyAsync());
    }

    /// <summary>
    /// Прерванная сверка продолжается с той фразы, на которой остановилась: сделанные
    /// второй раз не оплачиваются
    /// </summary>
    [Fact]
    public async Task InterruptedCheckContinues()
    {
        // Arrange
        var db = await Db();
        var chat = new FakeChatClient();
        var service = Service(db, chat);
        var jobId = await service.Start(_seed.Id, _oleg.Id);
        var job = await db.AdminJobs.SingleAsync();
        var done = new ModelCheckPhraseDto { Phrase = "уже сверено", Answer = "Готово.", ToolHit = true };
        job.Done = 2;
        job.Report = JsonSerializer.Serialize(ModelCheckReport.Build(Phrases, [done, done]), AdminJobService.ReportJson);
        await db.SaveChangesAsync();

        // Act
        await service.Run(jobId, null);

        // Assert
        Assert.Equal(Phrases - 2, chat.Calls);
        Assert.Equal(Phrases, Report(await db.AdminJobs.AsNoTracking().SingleAsync()).Results.Length);
    }

    /// <summary>Законченная сверка повтором команды не запускается заново</summary>
    [Fact]
    public async Task FinishedCheckIsNotRepeated()
    {
        // Arrange
        var db = await Db();
        var chat = new FakeChatClient();
        var service = Service(db, chat);
        var jobId = await service.Start(_seed.Id, _oleg.Id);
        await service.Run(jobId, null);
        var calls = chat.Calls;

        // Act
        await service.Run(jobId, null);

        // Assert
        Assert.Equal(calls, chat.Calls);
    }

    /// <summary>
    /// Аренда команды продлевается перед каждой фразой: сверка идёт минутами, и без
    /// продления воркер очереди взял бы её второй раз посреди работы
    /// </summary>
    [Fact]
    public async Task HandlerExtendsLeaseBeforeEachPhrase()
    {
        // Arrange
        var db = await Db();
        var service = Service(db, new FakeChatClient());
        var jobId = await service.Start(_seed.Id, _oleg.Id);
        var lease = new NoLease();

        // Act
        await new PetTemplate.Services.LocalQueueHandlers.ModelCheckCommandHandler(service, lease)
            .Handle(new ModelCheckCommand { JobId = jobId }, CancellationToken.None);

        // Assert
        Assert.True(lease.Extended >= Phrases);
    }

    /// <summary>
    /// Раздел проекта попадает в мир сверки сам (спец. assistant, 7.1): его фразы — в наборе
    /// после фраз ядра, его инструменты и операции — в каталоге мира. План применяется
    /// в транзакции мира точками сохранения и откатывается вместе с ней: в базе ни плана,
    /// ни прогона
    /// </summary>
    [Fact]
    public async Task SectionIsCheckedInWorld()
    {
        // Arrange
        var db = await Db();
        var store = new TestNoteStore();
        var section = new TestNoteSection(store);
        var core = ModelCheckPhrase.Embedded().Count;
        var chat = new FakeChatClient();
        for (var i = 0; i < core; i++) chat.Says("Готово.");
        chat.Calling("create_test_note", new { title = "Купить хлеб" })
            .Calling("apply_plan", new { })
            .Says("Заметка заведена.");
        var service = Service(db, chat, section);
        var jobId = await service.Start(_seed.Id, _oleg.Id);

        // Act
        await service.Run(jobId, null);

        // Assert
        var result = Report(await db.AdminJobs.AsNoTracking().SingleAsync()).Results[core];
        Assert.Equal((TestNoteSection.CheckPhrase, true, (string?)null), (result.Phrase, result.ToolHit, result.Error));
        Assert.Equal(1, store.Writes);
        Assert.False(await db.AssistantPlans.AnyAsync());
        Assert.False(await db.AssistantRuns.AnyAsync());
        Assert.Equal(_seed.Id, (await db.AiModels.AsNoTracking().SingleAsync()).Id);
    }

    private static ModelCheckService Service(ApplicationContext db, FakeChatClient chat,
        params IAssistantSection[] sections) =>
        new(db, new FakeModelChatClients(chat), new AdminJobService(db), AssistantWorld.EmptyHelp(),
            new NullEmbedder(),
            Options.Create(new AssistantOptions()),
            Options.Create(new SearchOptions()),
            sections,
            NullLogger<ModelCheckService>.Instance);

    private async Task<ApplicationContext> Db()
    {
        var (_, db) = await Given.ApiClient(x =>
        {
            x.Profiles.Add(_oleg);
            x.AiModels.Add(_seed);
        });

        return db;
    }

    private static ModelCheckReportDto Report(AdminJob job) =>
        JsonSerializer.Deserialize<ModelCheckReportDto>(job.Report!, AdminJobService.ReportJson)!;
}
