using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Billing;

/// <summary>
/// Лимит расхода ассистента на пространство (спец. assistant, 13.6): расход всех его
/// участников за сутки и за календарный месяц по UTC — токены и деньги по строкам
/// <see cref="RunSpend"/> и расшифровкам голоса — против потолков пространства. Свой лимит — строка
/// <see cref="WorkspaceAssistantLimit"/>, правится в админке; нет строки — умолчание
/// из конфигурации (<see cref="SpendOptions.DefaultLimit"/>).
///
/// Лимит мягкий: проверяется до старта хода, а расход хода пишется в его конце.
/// Поэтому ходы, начатые, пока потолок не достигнут, — у разных участников или
/// у одного в очереди треда — доходят до конца и могут перешагнуть его на свой расход;
/// следующий <c>Send</c> уже получит отказ. Строгий лимит потребовал бы резерва
/// до ответа модели, а стоимость хода до первого обращения к ней неизвестна (13.6)
/// </summary>
public class WorkspaceLimits(ApplicationContext db, IOptions<SpendOptions> options)
{
    /// <summary>
    /// Действующий лимит пространства: свой, если он есть, иначе умолчание.
    /// Своя строка действует целиком — пустое поле в ней значит «без потолка»
    /// </summary>
    public async Task<AssistantLimitOptions> Effective(Guid workspaceId, CancellationToken ct)
    {
        var own = await db.WorkspaceAssistantLimits
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);

        return own == null ? options.Value.DefaultLimit : Of(own);
    }

    public static AssistantLimitOptions Of(WorkspaceAssistantLimit limit) => new()
    {
        TokensPerDay = limit.TokensPerDay,
        TokensPerMonth = limit.TokensPerMonth,
        CostPerDay = limit.CostPerDay,
        CostPerMonth = limit.CostPerMonth
    };

    /// <summary>Начало суток по UTC, в которые попадает <paramref name="now"/></summary>
    public static DateTime DayStart(DateTime now) => DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

    /// <summary>Начало календарного месяца по UTC</summary>
    public static DateTime MonthStart(DateTime now) => new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Расход пространства с <paramref name="since"/> — все участники вместе, токены
    /// входящие и исходящие: ходы (<see cref="RunSpend"/>) и расшифровки голоса —
    /// их тоже просил человек, и минута надиктовки стоит денег так же, как ход.
    /// Остальной служебный расход (компакция) сюда не входит: его пространство
    /// не просило, и лимит, съеденный резюме, было бы не объяснить
    /// </summary>
    public async Task<(long Tokens, decimal Cost)> Usage(Guid workspaceId, DateTime since, CancellationToken ct)
    {
        var runs = await db.RunSpend
            .Where(x => x.WorkspaceId == workspaceId && x.CreatedUtc >= since)
            .GroupBy(_ => 1)
            .Select(x => new { Tokens = x.Sum(s => s.TokensIn + s.TokensOut), Cost = x.Sum(s => s.Cost) })
            .SingleOrDefaultAsync(ct);

        var voice = await db.ServiceSpend
            .Where(x => x.WorkspaceId == workspaceId
                        && x.Kind == ServiceSpendKind.Transcription
                        && x.CreatedUtc >= since)
            .GroupBy(_ => 1)
            .Select(x => new { Tokens = x.Sum(s => s.TokensIn + s.TokensOut), Cost = x.Sum(s => s.Cost) })
            .SingleOrDefaultAsync(ct);

        return ((runs?.Tokens ?? 0) + (voice?.Tokens ?? 0), (runs?.Cost ?? 0m) + (voice?.Cost ?? 0m));
    }

    /// <summary>
    /// Достигнут ли лимит — проверка на входе в ход (<c>Send</c>). Отказ — какой потолок
    /// сработал: суточный раньше месячного, он ближе к «попробуйте позже».
    /// <c>null</c> — ходить можно. Без потолков база не спрашивается вовсе
    /// </summary>
    public async Task<ErrorMessage?> Check(Guid workspaceId, DateTime now, CancellationToken ct)
    {
        var limit = await Effective(workspaceId, ct);

        if (limit.TokensPerDay != null || limit.CostPerDay != null)
        {
            var (tokens, cost) = await Usage(workspaceId, DayStart(now), ct);
            if (tokens >= limit.TokensPerDay || cost >= limit.CostPerDay) return AssistantErrors.DailyLimitReached;
        }

        if (limit.TokensPerMonth != null || limit.CostPerMonth != null)
        {
            var (tokens, cost) = await Usage(workspaceId, MonthStart(now), ct);
            if (tokens >= limit.TokensPerMonth || cost >= limit.CostPerMonth) return AssistantErrors.MonthlyLimitReached;
        }

        return null;
    }
}
