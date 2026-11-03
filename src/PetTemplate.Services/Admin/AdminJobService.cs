using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Запуски из админки (спец. admin, 15.3 и 16.3): сверка модели и автооценка. Здесь —
/// то, что у них общее: завести запуск с правилом «один запуск одного вида за раз»,
/// отметить отработанное, закрыть, показать.
///
/// Отказы — текстом, без кодов: админка их не переводит (спец. admin, 3.5)
/// </summary>
public class AdminJobService(ApplicationContext db)
{
    /// <summary>
    /// Запуск, не закончившийся за столько, считается брошенным: процесс упал, а очередь
    /// уже не вернёт его команды. Новый запуск закрывает его — иначе упавший запер бы
    /// кнопку навсегда. Самая долгая честная работа — сверка в два десятка ходов на
    /// медленной карте — укладывается с запасом
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    /// <summary>Список на экране — последние; старые читать незачем</summary>
    private const int ListLimit = 20;

    /// <summary>Длина <c>AdminJob.Error</c> в базе</summary>
    private const int ErrorMaxLength = 500;

    public static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Завести запуск вместе с тем, что уже лежит в контексте (команды очереди): одним
    /// сохранением, чтобы запуск без команд или команды без запуска не случались.
    /// Второй идущий того же вида — отказ текстом: его держит частичный уникальный индекс,
    /// а проверка перед вставкой нужна только ради понятного отказа
    /// </summary>
    public async Task Start(AdminJob job, CancellationToken ct = default)
    {
        await CloseStale(job.Kind, ct);

        if (await db.AdminJobs.AnyAsync(x => x.Kind == job.Kind && x.Status == AdminJobStatus.Running, ct))
            throw new AdminRejectionException(Busy(job.Kind));

        db.AdminJobs.Add(job);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsDuplicate(e, AdminJobConfiguration.RunningIndex))
        {
            throw new AdminRejectionException(Busy(job.Kind));
        }
    }

    /// <summary>
    /// Отметить отработанное одной командой — атомарным <c>UPDATE</c>: чтение с записью
    /// потеряли бы соседа, если команд оценки однажды станет несколько циклов. Последняя
    /// отметка закрывает запуск. Счёт «не меньше поставленного», а не «ровно»: повтор
    /// команды после падения может отметиться дважды, и запуск лучше закрыть на шаг
    /// раньше, чем навсегда оставить открытым
    /// </summary>
    public async Task Advance(
        Guid jobId,
        int done,
        int skipped,
        int failed,
        ServiceSpend? spend,
        string? error,
        CancellationToken ct = default)
    {
        var tokensIn = spend?.TokensIn ?? 0;
        var tokensCached = spend?.TokensCached ?? 0;
        var tokensOut = spend?.TokensOut ?? 0;
        var cost = spend?.Cost ?? 0;
        var message = Shorten(error);

        await db.AdminJobs
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(j => j.Done, j => j.Done + done)
                .SetProperty(j => j.Skipped, j => j.Skipped + skipped)
                .SetProperty(j => j.Failed, j => j.Failed + failed)
                .SetProperty(j => j.TokensIn, j => j.TokensIn + tokensIn)
                .SetProperty(j => j.TokensCached, j => j.TokensCached + tokensCached)
                .SetProperty(j => j.TokensOut, j => j.TokensOut + tokensOut)
                .SetProperty(j => j.Cost, j => j.Cost + cost)
                .SetProperty(j => j.Error, j => message ?? j.Error), ct);

        var now = DateTime.UtcNow;
        await db.AdminJobs
            .Where(x => x.Id == jobId
                        && x.Status == AdminJobStatus.Running
                        && x.Done + x.Skipped + x.Failed >= x.Planned)
            .ExecuteUpdateAsync(x => x
                .SetProperty(j => j.Status, AdminJobStatus.Done)
                .SetProperty(j => j.FinishedUtc, now), ct);
    }

    public async Task<AdminJobDto[]> List(AdminJobKind kind, CancellationToken ct = default) =>
        await Rows(db.AdminJobs.Where(x => x.Kind == kind))
            .OrderByDescending(x => x.CreatedUtc)
            .Take(ListLimit)
            .ToArrayAsync(ct);

    /// <summary>Запуск с отчётом сверки; <c>null</c> — такого запуска этого вида нет</summary>
    public async Task<AdminJobDto?> Get(Guid id, AdminJobKind kind, CancellationToken ct = default)
    {
        var job = await Rows(db.AdminJobs.Where(x => x.Id == id && x.Kind == kind)).SingleOrDefaultAsync(ct);
        if (job == null) return null;

        var report = await db.AdminJobs
            .Where(x => x.Id == id)
            .Select(x => x.Report)
            .SingleAsync(ct);
        if (report != null) job.Report = JsonSerializer.Deserialize<ModelCheckReportDto>(report, ReportJson);

        return job;
    }

    /// <summary>Длина колонки — с запасом под текст отказа поставщика</summary>
    public static string? Shorten(string? error) =>
        error == null ? null : error.Length <= ErrorMaxLength ? error : error[..(ErrorMaxLength - 1)] + "…";

    /// <summary>
    /// Брошенный запуск (<see cref="StaleAfter"/>) закрывается отказом. Отдельным
    /// сохранением, до вставки нового: в одном оба упёрлись бы в уникальный индекс
    /// в том порядке, в каком их отправит EF
    /// </summary>
    private async Task CloseStale(AdminJobKind kind, CancellationToken ct)
    {
        var threshold = DateTime.UtcNow - StaleAfter;
        var stale = await db.AdminJobs
            .Where(x => x.Kind == kind && x.Status == AdminJobStatus.Running && x.CreatedUtc < threshold)
            .ToListAsync(ct);
        if (stale.Count == 0) return;

        foreach (var job in stale)
        {
            job.Status = AdminJobStatus.Failed;
            job.FinishedUtc = DateTime.UtcNow;
            job.Error = $"Не закончился за {StaleAfter.TotalHours:0} ч — закрыт новым запуском";
        }

        await db.SaveChangesAsync(ct);
    }

    private IQueryable<AdminJobDto> Rows(IQueryable<AdminJob> jobs) =>
        jobs.Select(x => new AdminJobDto
        {
            Id = x.Id,
            Kind = x.Kind,
            ModelId = x.ModelId,
            ModelName = x.Model.DisplayName,
            StartedBy = db.Profiles
                .Where(p => p.Id == x.StartedByUserId)
                .Select(p => p.UserName)
                .FirstOrDefault(),
            CreatedUtc = x.CreatedUtc,
            FinishedUtc = x.FinishedUtc,
            Status = x.Status,
            Planned = x.Planned,
            Done = x.Done,
            Skipped = x.Skipped,
            Failed = x.Failed,
            TokensIn = x.TokensIn,
            TokensCached = x.TokensCached,
            TokensOut = x.TokensOut,
            Cost = x.Cost,
            Error = x.Error
        });

    private static string Busy(AdminJobKind kind) => kind == AdminJobKind.ModelCheck
        ? "Сверка уже идёт — дождитесь её конца: одна сверка за раз"
        : "Автооценка уже идёт — дождитесь её конца: два запуска оплатили бы одни и те же диалоги дважды";
}
