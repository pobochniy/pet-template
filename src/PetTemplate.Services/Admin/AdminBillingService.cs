using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Admin;

/// <summary>
/// «Расход» в админке (спец. admin, 14; assistant, 13.5–13.6): во что обходится ассистент,
/// сходится ли расчёт с фактом поставщика, наш счёт у поставщика и лимиты пространств.
/// Агрегаты — запросами к базе (<c>GROUP BY</c>), без таблицы итогов: пока ходов сотни
/// в день, считать дешевле, чем держать вторую копию правды.
///
/// Без баланса: в Lad здесь же сводились списания с кошельков и наценка, в шаблоне
/// расход только учитывается.
///
/// Отказы — текстом, без кодов: админка их не переводит (спец. admin, 3.5)
/// </summary>
public class AdminBillingService(
    ApplicationContext db,
    ProviderLedger ledger,
    WorkspaceLimits limits,
    IOptions<SpendOptions> options)
{
    /// <summary>Период по умолчанию — «за 30 дней»</summary>
    private const int DefaultDays = 30;

    /// <summary>Разрез по пространствам — самые расходные; хвост без денег не читают</summary>
    private const int WorkspacesLimit = 100;

    /// <summary>Длина <c>ProviderLedgerEntry.Comment</c> в базе</summary>
    private const int CommentMaxLength = 500;

    /// <param name="from">Первый день по UTC; по умолчанию — 30 дней до <paramref name="to"/></param>
    /// <param name="to">Последний день по UTC, включительно; по умолчанию — сегодня</param>
    /// <param name="modelId">Сужает верхние строки и разрез по пространствам</param>
    public async Task<AdminBillingSummaryDto> Summary(
        DateOnly? from,
        DateOnly? to,
        Guid? modelId,
        CancellationToken ct = default)
    {
        var (first, last) = Period(from, to);
        var since = ProviderLedger.Utc(first);
        var until = ProviderLedger.Utc(last.AddDays(1));

        // Расход хода пишется в момент его завершения (13.5) — по этому времени ход
        // и попадает в период, вместе со своими деньгами
        var runs = db.RunSpend.Where(x => x.CreatedUtc >= since && x.CreatedUtc < until);
        var spends = db.ServiceSpend.Where(x => x.CreatedUtc >= since && x.CreatedUtc < until);

        var models = await Models(runs, spends, ct);
        var picked = modelId == null ? models : models.Where(x => x.ModelId == modelId).ToArray();
        if (modelId != null && picked.Length == 0) throw new ArgumentException("Такой модели нет");

        // Служебные расходы — наши, в лимит пространства не идут (кроме расшифровки голоса:
        // её просил человек, 13.6). Пока вызовов не было,
        // строки нулевые, но стоят на месте: их появление не должно выглядеть
        // как разъехавшаяся формула
        var service = await Service(modelId == null ? spends : spends.Where(x => x.ModelId == modelId), ct);
        var serviceCost = service.Sum(x => x.Cost);
        var cost = picked.Sum(x => x.Cost);

        if (modelId != null) runs = runs.Where(x => x.ModelId == modelId);

        var providers = picked.Select(x => x.Provider).Distinct().ToList();
        if (modelId == null)
        {
            // Счёт у поставщика ведётся и тогда, когда ходов за период не было
            providers = providers
                .Union(await db.AiModels.Select(x => x.Provider).Distinct().ToListAsync(ct))
                .Union(await db.ProviderLedger.Select(x => x.Provider).Distinct().ToListAsync(ct))
                .Order()
                .ToList();
        }

        return new AdminBillingSummaryDto
        {
            From = first,
            To = last,
            ModelId = modelId,
            Currency = options.Value.Currency,
            Runs = picked.Sum(x => x.Runs),
            Tokens = picked.Sum(x => x.Tokens),
            Cost = cost,
            ServiceCost = serviceCost,
            Service = service,
            TotalCost = cost + serviceCost,
            DefaultLimit = Dto(options.Value.DefaultLimit),
            Models = models,
            Workspaces = await Workspaces(runs, ct),
            Providers = await Providers(providers, since, until, ct)
        };
    }

    /// <summary>
    /// Пространство в «Расходе»: действующий лимит и сколько от него съедено сейчас,
    /// кто из участников сколько потратил за период. <c>null</c> — пространства нет
    /// </summary>
    public async Task<AdminBillingWorkspaceCardDto?> Workspace(
        Guid workspaceId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var name = await db.Workspaces
            .Where(x => x.Id == workspaceId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(ct);
        if (name == null) return null;

        var (first, last) = Period(from, to);
        var since = ProviderLedger.Utc(first);
        var until = ProviderLedger.Utc(last.AddDays(1));

        var own = await db.WorkspaceAssistantLimits
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        var defaults = options.Value.DefaultLimit;

        // Одно «сейчас» на обе отметки: на стыке суток и месяца две разные взяли бы
        // разные отрезки
        var now = DateTime.UtcNow;
        var (dayTokens, dayCost) = await limits.Usage(workspaceId, WorkspaceLimits.DayStart(now), ct);
        var (monthTokens, monthCost) = await limits.Usage(workspaceId, WorkspaceLimits.MonthStart(now), ct);

        var users = await db.RunSpend
            .Where(x => x.WorkspaceId == workspaceId && x.CreatedUtc >= since && x.CreatedUtc < until)
            .GroupBy(x => x.UserId)
            .Select(x => new AdminBillingUserDto
            {
                UserId = x.Key,
                Runs = x.Count(),
                Tokens = x.Sum(s => s.TokensIn + s.TokensOut),
                Cost = x.Sum(s => s.Cost)
            })
            .ToArrayAsync(ct);

        var people = users.Select(x => x.UserId).Append(own?.UpdatedByUserId ?? Guid.Empty).ToList();
        var names = await Names(people, ct);
        foreach (var user in users) user.Name = names.GetValueOrDefault(user.UserId);

        return new AdminBillingWorkspaceCardDto
        {
            WorkspaceId = workspaceId,
            WorkspaceName = name,
            Currency = options.Value.Currency,
            OwnLimit = own == null ? null : Dto(WorkspaceLimits.Of(own)),
            DefaultLimit = Dto(defaults),
            Limit = Dto(own == null ? defaults : WorkspaceLimits.Of(own)),
            Today = new AdminSpendUsageDto { Tokens = dayTokens, Cost = dayCost },
            Month = new AdminSpendUsageDto { Tokens = monthTokens, Cost = monthCost },
            From = first,
            To = last,
            Users = users.OrderByDescending(x => x.Cost).ThenByDescending(x => x.Tokens).ToArray(),
            LimitUpdatedUtc = own?.UpdatedUtc,
            LimitUpdatedBy = own?.UpdatedByUserId is { } adminId ? names.GetValueOrDefault(adminId) : null
        };
    }

    /// <summary>
    /// Свой лимит пространства (роль <c>Billing</c>): заменяет умолчание целиком, пустое
    /// поле — без потолка. Действует со следующего <c>Send</c>. <c>false</c> — пространства нет
    /// </summary>
    public async Task<bool> SetLimit(
        Guid workspaceId,
        AdminAssistantLimitDto dto,
        Guid adminId,
        CancellationToken ct = default)
    {
        if (dto.TokensPerDay < 0 || dto.TokensPerMonth < 0 || dto.CostPerDay < 0 || dto.CostPerMonth < 0)
            throw new ArgumentException("Лимит не может быть отрицательным; без потолка — пустое поле");

        if (!await db.Workspaces.AnyAsync(x => x.Id == workspaceId, ct)) return false;

        var limit = await db.WorkspaceAssistantLimits.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        if (limit == null)
        {
            limit = new WorkspaceAssistantLimit { WorkspaceId = workspaceId };
            db.WorkspaceAssistantLimits.Add(limit);
        }

        limit.TokensPerDay = dto.TokensPerDay;
        limit.TokensPerMonth = dto.TokensPerMonth;
        limit.CostPerDay = dto.CostPerDay;
        limit.CostPerMonth = dto.CostPerMonth;
        limit.UpdatedUtc = DateTime.UtcNow;
        limit.UpdatedByUserId = adminId;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsDuplicate(e, DbErrors.WorkspaceLimitKey))
        {
            // Первую строку пространству одновременно завёл другой админ: молча
            // перезаписать его потолок своим было бы сюрпризом для обоих
            throw new ArgumentException("Лимит этого пространства только что поменял другой админ — откройте его заново");
        }

        return true;
    }

    /// <summary>Вернуть пространство к умолчанию из конфигурации. <c>false</c> — пространства нет</summary>
    public async Task<bool> ResetLimit(Guid workspaceId, CancellationToken ct = default)
    {
        if (!await db.Workspaces.AnyAsync(x => x.Id == workspaceId, ct)) return false;

        await db.WorkspaceAssistantLimits.Where(x => x.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);

        return true;
    }

    /// <summary>
    /// Платёж поставщику или факт расхода за период (роль <c>Billing</c>). Строки
    /// не правятся: факт правится записью с тем же периодом, ошибочный платёж —
    /// платежом с минусом и причиной. Отвечает идентификатором записи
    /// </summary>
    public async Task<Guid> Record(AdminLedgerEntryDto dto, Guid adminId, CancellationToken ct = default)
    {
        var provider = dto.Provider?.Trim() ?? string.Empty;
        if (provider.Length == 0) throw new ArgumentException("Не указан поставщик");
        if (!await db.AiModels.AnyAsync(x => x.Provider == provider, ct))
            throw new ArgumentException($"В справочнике моделей нет поставщика «{provider}»");

        var comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        if (comment?.Length > CommentMaxLength)
            throw new ArgumentException($"Комментарий длиннее {CommentMaxLength} символов");

        var now = DateTime.UtcNow;
        var entry = new ProviderLedgerEntry
        {
            Id = Guid.CreateVersion7(now),
            Provider = provider,
            Kind = dto.Kind,
            Amount = dto.Amount,
            Comment = comment,
            CreatedUtc = now,
            ActorUserId = adminId
        };

        switch (dto.Kind)
        {
            case ProviderLedgerKind.Payment:
                if (dto.PeriodFrom != null || dto.PeriodTo != null)
                    throw new ArgumentException("У платежа периода нет — он есть только у факта расхода");
                if (dto.Amount == 0) throw new ArgumentException("Сумма не может быть нулевой");
                if (dto.Amount < 0 && comment == null)
                    throw new ArgumentException("Платёж с минусом исправляет ошибочный — напишите, какой");
                break;

            case ProviderLedgerKind.ActualSpend:
                if (dto.PeriodFrom is not { } periodFrom || dto.PeriodTo is not { } periodTo)
                    throw new ArgumentException("У факта расхода нужен период");
                if (periodFrom > periodTo) throw new ArgumentException("Начало периода позже его конца");
                if (periodTo > DateOnly.FromDateTime(now))
                    throw new ArgumentException("Факт за дни, которые ещё не прошли, не вводится");
                if (dto.Amount < 0) throw new ArgumentException("Расход не может быть отрицательным");

                entry.PeriodFrom = ProviderLedger.Utc(periodFrom);
                entry.PeriodTo = ProviderLedger.Utc(periodTo.AddDays(1));

                // Тот же период — правка; задетый чужой — отказ: два факта за одни дни
                // сложились бы, и расхождение соврало бы вдвое
                var overlap = (await ledger.Facts(ct)).FirstOrDefault(x =>
                    x.Provider == provider
                    && x.PeriodFrom < entry.PeriodTo && x.PeriodTo > entry.PeriodFrom
                    && (x.PeriodFrom != entry.PeriodFrom || x.PeriodTo != entry.PeriodTo));
                if (overlap != null)
                {
                    throw new ArgumentException(
                        $"Период задевает факт за {Days(overlap)}: исправьте тот факт или возьмите дни без пересечения");
                }

                break;

            default:
                throw new ArgumentException("Неизвестный вид записи");
        }

        db.ProviderLedger.Add(entry);
        await db.SaveChangesAsync(ct);

        return entry.Id;
    }

    private static (DateOnly First, DateOnly Last) Period(DateOnly? from, DateOnly? to)
    {
        var last = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var first = from ?? last.AddDays(1 - DefaultDays);
        if (first > last) throw new ArgumentException("Начало периода позже его конца");

        return (first, last);
    }

    /// <summary>Служебные расходы по виду — все виды, и те, у которых вызовов не было</summary>
    private static async Task<AdminServiceSpendDto[]> Service(IQueryable<ServiceSpend> spends, CancellationToken ct)
    {
        var byKind = await spends
            .GroupBy(x => x.Kind)
            .Select(x => new
            {
                Kind = x.Key,
                Calls = x.Count(),
                Tokens = x.Sum(s => s.TokensIn + s.TokensOut),
                Cost = x.Sum(s => s.Cost)
            })
            .ToDictionaryAsync(x => x.Kind, ct);

        return Enum.GetValues<ServiceSpendKind>()
            .Select(kind => new AdminServiceSpendDto
            {
                Kind = kind,
                Calls = byKind.GetValueOrDefault(kind)?.Calls ?? 0,
                Tokens = byKind.GetValueOrDefault(kind)?.Tokens ?? 0,
                Cost = byKind.GetValueOrDefault(kind)?.Cost ?? 0
            })
            .ToArray();
    }

    /// <summary>
    /// Разрез по моделям — по всем моделям справочника, в порядке обращения: ходы
    /// и рядом служебные вызовы той же модели. Читается из строк расхода: снос
    /// пространства их не трогает, и прошлый период не худеет
    /// </summary>
    private async Task<AdminBillingModelDto[]> Models(
        IQueryable<RunSpend> runs,
        IQueryable<ServiceSpend> spends,
        CancellationToken ct)
    {
        // Расшифровка голоса модели справочника не имеет (13.5): в разрез по моделям
        // она не входит, её видно строкой своего вида в служебных
        var service = await spends
            .Where(x => x.ModelId != null)
            .GroupBy(x => x.ModelId!.Value)
            .Select(x => new { Id = x.Key, Tokens = x.Sum(s => s.TokensIn + s.TokensOut), Cost = x.Sum(s => s.Cost) })
            .ToDictionaryAsync(x => x.Id, ct);

        var done = await runs
            .GroupBy(x => x.ModelId)
            .Select(x => new
            {
                Id = x.Key,
                Runs = x.Count(),
                Tokens = x.Sum(s => s.TokensIn + s.TokensOut),
                Cost = x.Sum(s => s.Cost)
            })
            .ToDictionaryAsync(x => x.Id, ct);

        var catalog = await db.AiModels
            .AsNoTracking()
            .OrderBy(x => x.Priority)
            .Select(x => new { x.Id, x.DisplayName, x.Provider })
            .ToListAsync(ct);

        return catalog
            .Select(x => new AdminBillingModelDto
            {
                ModelId = x.Id,
                DisplayName = x.DisplayName,
                Provider = x.Provider,
                Runs = done.GetValueOrDefault(x.Id)?.Runs ?? 0,
                Tokens = done.GetValueOrDefault(x.Id)?.Tokens ?? 0,
                Cost = done.GetValueOrDefault(x.Id)?.Cost ?? 0,
                ServiceTokens = service.GetValueOrDefault(x.Id)?.Tokens ?? 0,
                ServiceCost = service.GetValueOrDefault(x.Id)?.Cost ?? 0
            })
            .ToArray();
    }

    /// <summary>
    /// Кто съедает счёт — самые расходные пространства. Удалённое остаётся строкой
    /// без имени: его расход ушёл поставщику и в итогах есть
    /// </summary>
    private async Task<AdminBillingWorkspaceDto[]> Workspaces(IQueryable<RunSpend> runs, CancellationToken ct)
    {
        var top = await runs
            .GroupBy(x => x.WorkspaceId)
            .Select(x => new AdminBillingWorkspaceDto
            {
                WorkspaceId = x.Key,
                Runs = x.Count(),
                Tokens = x.Sum(s => s.TokensIn + s.TokensOut),
                Cost = x.Sum(s => s.Cost)
            })
            .OrderByDescending(x => x.Cost)
            .ThenByDescending(x => x.Tokens)
            .Take(WorkspacesLimit)
            .ToArrayAsync(ct);

        var ids = top.Select(x => x.WorkspaceId).ToList();
        var names = await db.Workspaces
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var own = await db.WorkspaceAssistantLimits
            .Where(x => ids.Contains(x.WorkspaceId))
            .Select(x => x.WorkspaceId)
            .ToListAsync(ct);

        foreach (var row in top)
        {
            row.WorkspaceName = names.GetValueOrDefault(row.WorkspaceId);
            row.OwnLimit = own.Contains(row.WorkspaceId);
        }

        return top;
    }

    private async Task<AdminBillingProviderDto[]> Providers(
        IReadOnlyCollection<string> providers,
        DateTime since,
        DateTime until,
        CancellationToken ct)
    {
        var facts = await ledger.Facts(ct);
        var names = await Names(facts.Select(x => x.ActorUserId ?? Guid.Empty).ToList(), ct);

        var result = new List<AdminBillingProviderDto>();
        foreach (var provider in providers)
        {
            var touching = facts
                .Where(x => x.Provider == provider && x.PeriodFrom < until && x.PeriodTo > since)
                .ToList();

            // Расчёт — ровно за дни фактов: сравнивать сентябрьский факт
            // с «последними 30 днями» значило бы получить расхождение на ровном месте
            var estimate = 0m;
            foreach (var fact in touching)
                estimate += await ledger.Estimate(provider, fact.PeriodFrom!.Value, fact.PeriodTo, ct);

            decimal? factAmount = touching.Count > 0 ? touching.Sum(x => x.Amount) : null;

            result.Add(new AdminBillingProviderDto
            {
                Provider = provider,
                Facts = touching
                    .Select(x => new AdminLedgerFactDto
                    {
                        Id = x.Id,
                        PeriodFrom = DateOnly.FromDateTime(x.PeriodFrom!.Value),
                        PeriodTo = DateOnly.FromDateTime(x.PeriodTo!.Value).AddDays(-1),
                        Amount = x.Amount,
                        Comment = x.Comment,
                        CreatedUtc = x.CreatedUtc,
                        ActorName = x.ActorUserId is { } actorId ? names.GetValueOrDefault(actorId) : null
                    })
                    .ToArray(),
                Fact = factAmount,
                Estimate = estimate,
                Discrepancy = estimate - factAmount,
                Account = await ledger.Account(provider, facts, ct)
            });
        }

        return result.ToArray();
    }

    /// <summary>Как назвать человека в «Расходе» — логином, как в «Аккаунтах»</summary>
    private async Task<Dictionary<Guid, string>> Names(List<Guid> ids, CancellationToken ct) =>
        await db.Profiles
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, Name = x.UserName })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private static AdminAssistantLimitDto Dto(AssistantLimitOptions limit) => new()
    {
        TokensPerDay = limit.TokensPerDay,
        TokensPerMonth = limit.TokensPerMonth,
        CostPerDay = limit.CostPerDay,
        CostPerMonth = limit.CostPerMonth
    };

    private static string Days(ProviderLedgerEntry fact) =>
        $"{fact.PeriodFrom:dd.MM.yyyy}–{fact.PeriodTo!.Value.AddDays(-1):dd.MM.yyyy}";
}
