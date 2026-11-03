using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Check;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Assistant.Sections;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Commands;
using PetTemplate.Services.Help;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Сверка модели из админки (спец. admin, 15.3): набор фраз против строки справочника —
/// в том числе выключенной, кандидата проверяют до включения.
///
/// Каждая фраза — настоящий ход в изолированном мире (<see cref="ModelCheckRunner"/>):
/// в боевую базу не уходит ничего, кроме самого запуска и его расходов. Фразы наши,
/// переписки в них нет, поэтому правило поставщиков автооценки (16.3) здесь не о чем
/// применять — годится любой. Размыкателю сверка не отчитывается.
///
/// Отказы — текстом, без кодов: админка их не переводит (спец. admin, 3.5)
/// </summary>
public class ModelCheckService(
    ApplicationContext db,
    IModelChatClients clients,
    AdminJobService jobs,
    HelpSearch help,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    IOptions<AssistantOptions> assistant,
    IOptions<SearchOptions> search,
    IEnumerable<IAssistantSection> sections,
    ILogger<ModelCheckService> logger)
{
    private readonly IReadOnlyList<IAssistantSection> _sections = [.. sections];

    /// <summary>Завести сверку и поставить её в очередь. Отвечает запуском</summary>
    public async Task<Guid> Start(Guid modelId, Guid adminId, CancellationToken ct = default)
    {
        var model = await db.AiModels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == modelId, ct)
                    ?? throw new AdminRejectionException("Такой модели нет");

        var phrases = ModelCheckPhrase.Of(_sections);
        var job = new AdminJob
        {
            Id = Guid.CreateVersion7(),
            Kind = AdminJobKind.ModelCheck,
            ModelId = model.Id,
            StartedByUserId = adminId,
            CreatedUtc = DateTime.UtcNow,
            Status = AdminJobStatus.Running,
            Planned = phrases.Count,
            Report = Serialize(ModelCheckReport.Build(phrases.Count, []))
        };

        db.Enqueue(new ModelCheckCommand { JobId = job.Id });
        await jobs.Start(job, ct);

        logger.LogInformation("Model check {JobId} of {ModelKey} started by {AdminId}", job.Id, model.Key, adminId);

        return job.Id;
    }

    /// <summary>
    /// Прогнать сверку — из обработчика очереди. Фразы по очереди, после каждой — одним
    /// сохранением строка расхода, счётчики и отчёт на сейчас: повтор команды после
    /// падения продолжает с той фразы, на которой остановились
    /// </summary>
    /// <param name="jobId">Запуск</param>
    /// <param name="extendLease">Продление аренды команды: сверка идёт минутами</param>
    /// <param name="ct">Остановка воркера</param>
    public async Task Run(Guid jobId, Func<Task>? extendLease, CancellationToken ct = default)
    {
        var job = await db.AdminJobs.SingleOrDefaultAsync(x => x.Id == jobId, ct);
        if (job is not { Status: AdminJobStatus.Running }) return;

        var model = await db.AiModels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == job.ModelId, ct);
        if (model == null)
        {
            Finish(job, AdminJobStatus.Failed, "Модели нет в справочнике");
            await db.SaveChangesAsync(ct);
            return;
        }

        var phrases = ModelCheckPhrase.Of(_sections);
        var results = (job.Report == null
                ? null
                : JsonSerializer.Deserialize<ModelCheckReportDto>(job.Report, AdminJobService.ReportJson))?.Results.ToList()
            ?? [];

        var chat = clients.For(model);
        var environment = new ModelCheckEnvironment
        {
            ConnectionString = db.Database.GetConnectionString()!,
            Assistant = assistant.Value,
            Search = search.Value,
            Embedder = embedder,
            Help = help.Current,
            Sections = _sections
        };
        for (var i = results.Count; i < phrases.Count; i++)
        {
            if (extendLease != null) await extendLease();

            var outcome = await ModelCheckRunner.RunOne(phrases[i], model, chat, environment, extendLease, ct);

            // Строка на фразу, на которой модель хоть что-то ответила: отказ до первого
            // ответа поставщику ничего не стоил
            var cost = 0m;
            if (outcome.Responses.Count > 0 || outcome.TokensIn + outcome.TokensOut > 0)
            {
                var spend = ServiceSpending.Of(ServiceSpendKind.ModelCheck, model,
                    outcome.TokensIn, outcome.TokensCached, outcome.TokensOut);
                db.ServiceSpend.Add(spend);
                cost = spend.Cost;

                job.TokensIn += spend.TokensIn;
                job.TokensCached += spend.TokensCached;
                job.TokensOut += spend.TokensOut;
                job.Cost += spend.Cost;
            }

            results.Add(outcome.ToResult(cost));
            job.Done = results.Count;
            job.Report = Serialize(ModelCheckReport.Build(phrases.Count, results));
            await db.SaveChangesAsync(ct);
        }

        Finish(job, AdminJobStatus.Done, null);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Model check {JobId} of {ModelKey} is done", job.Id, model.Key);
    }

    private static void Finish(AdminJob job, AdminJobStatus status, string? error)
    {
        job.Status = status;
        job.FinishedUtc = DateTime.UtcNow;
        job.Error = AdminJobService.Shorten(error);
    }

    private static string Serialize(ModelCheckReportDto report) =>
        JsonSerializer.Serialize(report, AdminJobService.ReportJson);
}
