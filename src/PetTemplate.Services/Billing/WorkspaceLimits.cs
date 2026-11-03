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
/// Поверх него — два суточных потолка из конфигурации: человека по всем его пространствам
/// (<see cref="SpendOptions.UserDailyLimit"/>; пространство заводит кто угодно, и лимит
/// пространства в одиночку обходился бы новыми пустыми) и всей установки
/// (<see cref="SpendOptions.InstallationDailyLimit"/>).
///
/// Резерв под ход (13.6): живой прогон и реплика, ждущая очереди треда, занимают
/// <see cref="AssistantOptions.TokensPerRun"/> — потолок хода — и его цену по самой дорогой
/// включённой модели, пока ход не закончится. Конец хода сверяет резерв по факту: строка
/// <see cref="RunSpend"/> пишется той же транзакцией, что и финальный статус, и резерв
/// уходит вместе с ним. Проверка и постановка хода идут под замком (<see cref="Hold"/>):
/// параллельные <c>Send</c> не проходят оба мимо резерва друг друга. Сверх потолка
/// выходит не больше одного хода — того, что прошёл последним
/// </summary>
public class WorkspaceLimits(
    ApplicationContext db,
    IOptions<SpendOptions> options,
    IOptions<AssistantOptions> assistant)
{
    /// <summary>Префикс ключей замка проверки лимита в <c>pg_advisory_lock</c></summary>
    private const string LockPrefix = "assistant-limit";

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
    /// Достигнут ли лимит — проверка на входе в ход (<c>Send</c>). Порядок: потолок
    /// установки (ассистент выключен для всех — <c>modelUnavailable</c>), лимит пространства —
    /// суточный раньше месячного, он ближе к «попробуйте позже», — и суточный потолок человека
    /// по всем его пространствам. <c>null</c> — ходить можно. Без потолков база
    /// не спрашивается вовсе
    /// </summary>
    public async Task<ErrorMessage?> Check(Guid workspaceId, Guid userId, DateTime now, CancellationToken ct)
    {
        var settings = options.Value;
        var day = DayStart(now);

        if (Capped(settings.InstallationDailyLimit))
        {
            var (tokens, cost) = Sum(await InstallationUsage(day, ct), await Reserved(null, null, ct));
            if (Reached(settings.InstallationDailyLimit, tokens, cost)) return AssistantErrors.ModelUnavailable;
        }

        var limit = await Effective(workspaceId, ct);
        var daily = limit.TokensPerDay != null || limit.CostPerDay != null;
        var monthly = limit.TokensPerMonth != null || limit.CostPerMonth != null;
        var reserved = daily || monthly ? await Reserved(workspaceId, null, ct) : (0, 0m);

        if (daily)
        {
            var (tokens, cost) = Sum(await Usage(workspaceId, day, ct), reserved);
            if (tokens >= limit.TokensPerDay || cost >= limit.CostPerDay) return AssistantErrors.DailyLimitReached;
        }

        if (monthly)
        {
            var (tokens, cost) = Sum(await Usage(workspaceId, MonthStart(now), ct), reserved);
            if (tokens >= limit.TokensPerMonth || cost >= limit.CostPerMonth) return AssistantErrors.MonthlyLimitReached;
        }

        if (Capped(settings.UserDailyLimit))
        {
            var (tokens, cost) = Sum(await UserUsage(userId, day, ct), await Reserved(null, userId, ct));
            if (Reached(settings.UserDailyLimit, tokens, cost)) return AssistantErrors.UserDailyLimitReached;
        }

        return null;
    }

    /// <summary>
    /// Резерв ходов, ещё не сверенных по факту (13.6): живые прогоны и реплики, ждущие
    /// очереди треда, — по потолку хода каждый. Ждущая реплика человека, которого убрали
    /// из пространства, хода не получит (12.1) и резерва не держит. Фильтры пустые —
    /// резерв всей установки
    /// </summary>
    public async Task<(long Tokens, decimal Cost)> Reserved(Guid? workspaceId, Guid? userId, CancellationToken ct)
    {
        var runs = db.AssistantRuns.Where(x => x.FinishedAtUtc == null);
        var waiting = db.AssistantMessages.Where(x => x.Role == AssistantMessageRole.User
                                                      && x.RunId == null
                                                      && db.UserInPermissions.Any(p => p.UserId == x.UserId
                                                          && p.WorkspaceId == x.WorkspaceId));
        if (workspaceId != null)
        {
            runs = runs.Where(x => x.WorkspaceId == workspaceId);
            waiting = waiting.Where(x => x.WorkspaceId == workspaceId);
        }

        if (userId != null)
        {
            runs = runs.Where(x => x.UserId == userId);
            waiting = waiting.Where(x => x.UserId == userId);
        }

        var count = await runs.CountAsync(ct) + await waiting.CountAsync(ct);
        if (count == 0) return (0, 0m);

        // Цена резерва — по самой дорогой ставке включённых моделей: какой моделью пойдёт
        // ход, до первого обращения неизвестно, и ошибаться надо в сторону «дороже» (13.5)
        var rate = await db.AiModels
            .Where(x => x.IsEnabled)
            .Select(x => x.CostIn > x.CostOut ? x.CostIn : x.CostOut)
            .OrderByDescending(x => x)
            .FirstOrDefaultAsync(ct);
        long tokens = (long)count * assistant.Value.TokensPerRun;

        return (tokens, tokens * rate / 1000m);
    }

    /// <summary>
    /// Замок проверки лимита (13.6): от проверки до записи прогона или реплики — резерва —
    /// второй <c>Send</c> того же пространства, того же человека (если у него потолок)
    /// или всей установки (если потолок у неё) ждёт. Иначе оба прочли бы расход без резерва
    /// соседа и прошли оба. Замок сессии Postgres (<c>pg_advisory_lock</c>), а не транзакции:
    /// <c>Send</c> переживает проигранную гонку за индекс треда, а транзакция после ошибки
    /// вставки уже не годна. Порядок ключей один у всех — установка, пространство, человек:
    /// взаимной блокировки нет
    /// </summary>
    public async Task<IAsyncDisposable> Hold(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        var settings = options.Value;
        var keys = new List<string>();
        if (Capped(settings.InstallationDailyLimit)) keys.Add(LockPrefix);
        keys.Add($"{LockPrefix}:workspace:{workspaceId}");
        if (Capped(settings.UserDailyLimit)) keys.Add($"{LockPrefix}:user:{userId}");

        var hold = new LimitHold(db);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            foreach (var key in keys)
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock(hashtextextended({key}, 0))", ct);
                hold.Taken.Add(key);
            }
        }
        catch
        {
            await hold.DisposeAsync();
            throw;
        }

        return hold;
    }

    /// <summary>
    /// Расход человека с <paramref name="since"/> — по всем пространствам: ходы и расшифровки
    /// голоса, как у лимита пространства (<see cref="Usage"/>)
    /// </summary>
    public async Task<(long Tokens, decimal Cost)> UserUsage(Guid userId, DateTime since, CancellationToken ct)
    {
        var runs = await db.RunSpend
            .Where(x => x.UserId == userId && x.CreatedUtc >= since)
            .GroupBy(_ => 1)
            .Select(x => new { Tokens = x.Sum(s => s.TokensIn + s.TokensOut), Cost = x.Sum(s => s.Cost) })
            .SingleOrDefaultAsync(ct);

        var voice = await db.ServiceSpend
            .Where(x => x.UserId == userId
                        && x.Kind == ServiceSpendKind.Transcription
                        && x.CreatedUtc >= since)
            .GroupBy(_ => 1)
            .Select(x => new { Tokens = x.Sum(s => s.TokensIn + s.TokensOut), Cost = x.Sum(s => s.Cost) })
            .SingleOrDefaultAsync(ct);

        return ((runs?.Tokens ?? 0) + (voice?.Tokens ?? 0), (runs?.Cost ?? 0m) + (voice?.Cost ?? 0m));
    }

    /// <summary>
    /// Расход всей установки с <paramref name="since"/>: ходы и весь служебный расход —
    /// компакция, оценка, сверка моделей. Потолку установки неважно, кто просил: это деньги,
    /// ушедшие поставщику
    /// </summary>
    public async Task<(long Tokens, decimal Cost)> InstallationUsage(DateTime since, CancellationToken ct)
    {
        var runs = await db.RunSpend
            .Where(x => x.CreatedUtc >= since)
            .GroupBy(_ => 1)
            .Select(x => new { Tokens = x.Sum(s => s.TokensIn + s.TokensOut), Cost = x.Sum(s => s.Cost) })
            .SingleOrDefaultAsync(ct);

        var service = await db.ServiceSpend
            .Where(x => x.CreatedUtc >= since)
            .GroupBy(_ => 1)
            .Select(x => new { Tokens = x.Sum(s => s.TokensIn + s.TokensOut), Cost = x.Sum(s => s.Cost) })
            .SingleOrDefaultAsync(ct);

        return ((runs?.Tokens ?? 0) + (service?.Tokens ?? 0), (runs?.Cost ?? 0m) + (service?.Cost ?? 0m));
    }

    private static (long Tokens, decimal Cost) Sum((long Tokens, decimal Cost) a, (long Tokens, decimal Cost) b) =>
        (a.Tokens + b.Tokens, a.Cost + b.Cost);

    private static bool Capped(AssistantDailyLimitOptions limit) =>
        limit.TokensPerDay != null || limit.CostPerDay != null;

    private static bool Reached(AssistantDailyLimitOptions limit, long tokens, decimal cost) =>
        tokens >= limit.TokensPerDay || cost >= limit.CostPerDay;

    /// <summary>Снятый замок — те же ключи в обратном порядке, затем соединение</summary>
    private sealed class LimitHold(ApplicationContext db) : IAsyncDisposable
    {
        public List<string> Taken { get; } = [];

        public async ValueTask DisposeAsync()
        {
            // Без токена запроса: отменённый запрос замок всё равно отпускает
            for (var i = Taken.Count - 1; i >= 0; i--)
            {
                await db.Database.ExecuteSqlAsync(
                    $"SELECT pg_advisory_unlock(hashtextextended({Taken[i]}, 0))", CancellationToken.None);
            }

            await db.Database.CloseConnectionAsync();
        }
    }
}
