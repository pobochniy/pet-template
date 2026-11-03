using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Журнал доступа к диалогам (спец. admin, 16.2): кто, когда, чей прогон открыл.
/// Пишет его <see cref="AdminDialogService"/> на каждое открытие, читает вкладка под
/// <c>AdminManagement</c> — смысл записи в том, что её смотрит не тот, кто её оставил.
///
/// Удалить запись нечем, кроме срока <see cref="AdminOptions.AccessLogRetentionDays"/>:
/// метода удаления здесь нет намеренно
/// </summary>
public class AdminAccessLogService(ApplicationContext db, IOptions<AdminOptions> options)
{
    private const int PageSize = 100;

    /// <summary>
    /// Запись об открытии — своим <c>SaveChanges</c>, до того, как содержимое уйдёт
    /// в ответ: не записалось — не показываем
    /// </summary>
    internal async Task Write(Guid adminId, Guid runId, Guid workspaceId, CancellationToken ct)
    {
        db.AdminAccessLog.Add(new AdminAccessLog
        {
            Id = Guid.CreateVersion7(),
            AdminUserId = adminId,
            RunId = runId,
            WorkspaceId = workspaceId,
            CreatedUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Плоский список, новые сверху, страницами; фильтр — по админу и периоду</summary>
    public async Task<AdminAccessLogPageDto> List(AdminAccessLogFilterDto filter, CancellationToken ct = default)
    {
        var entries = db.AdminAccessLog.AsNoTracking();

        if (filter.AdminUserId is { } adminId) entries = entries.Where(x => x.AdminUserId == adminId);

        if (filter.From is { } from)
        {
            var since = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entries = entries.Where(x => x.CreatedUtc >= since);
        }

        if (filter.To is { } to)
        {
            var until = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entries = entries.Where(x => x.CreatedUtc < until);
        }

        // Курсор — пара (время, Id): записи с одной меткой иначе терялись бы на
        // границе страницы
        if (filter.Before is { } before)
        {
            var cursor = await db.AdminAccessLog
                .Where(x => x.Id == before)
                .Select(x => new { x.CreatedUtc, x.Id })
                .SingleOrDefaultAsync(ct);
            if (cursor != null)
            {
                entries = entries.Where(x => x.CreatedUtc < cursor.CreatedUtc
                                             || (x.CreatedUtc == cursor.CreatedUtc && x.Id.CompareTo(cursor.Id) < 0));
            }
        }

        var page = await entries
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(PageSize)
            .Select(x => new AdminAccessLogEntryDto
            {
                Id = x.Id,
                CreatedUtc = x.CreatedUtc,
                AdminUserId = x.AdminUserId,
                // Журнал переживает и админа, и пространство — тогда строка без имени
                AdminName = db.Profiles
                    .Where(p => p.Id == x.AdminUserId)
                    .Select(p => p.UserName)
                    .FirstOrDefault(),
                RunId = x.RunId,
                WorkspaceId = x.WorkspaceId,
                WorkspaceName = db.Workspaces
                    .Where(w => w.Id == x.WorkspaceId)
                    .Select(w => w.Name)
                    .FirstOrDefault()
            })
            .ToArrayAsync(ct);

        return new AdminAccessLogPageDto
        {
            Entries = page,
            NextBefore = page.Length == PageSize ? page[^1].Id : null
        };
    }

    /// <summary>
    /// Суточная чистка: старше <see cref="AdminOptions.AccessLogRetentionDays"/>.
    /// Возвращает число удалённых строк
    /// </summary>
    public async Task<int> CleanupOld(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.AddDays(-options.Value.AccessLogRetentionDays);

        return await db.AdminAccessLog
            .Where(x => x.CreatedUtc < threshold)
            .ExecuteDeleteAsync(ct);
    }
}
