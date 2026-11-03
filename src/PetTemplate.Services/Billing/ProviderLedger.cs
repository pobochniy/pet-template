using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Billing;

/// <summary>
/// Наш счёт у поставщика и расчёт, с которым сверяется его факт (спец. assistant, 13.5;
/// admin, 14). Общий для сводки в админке и для периодической проверки с метриками:
/// цифра на экране и цифра в тревоге должны считаться одинаково.
///
/// Записей в счёте — единицы в месяц, поэтому «действующий факт» выбирается в памяти,
/// а не запросом
/// </summary>
public class ProviderLedger(ApplicationContext db)
{
    /// <summary>Начало дня по UTC — граница периода</summary>
    public static DateTime Utc(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>
    /// Действующие факты расхода: за каждый период — последний введённый, прежние
    /// остались следом правки. По возрастанию периода
    /// </summary>
    public async Task<ProviderLedgerEntry[]> Facts(CancellationToken ct = default)
    {
        var all = await db.ProviderLedger
            .AsNoTracking()
            .Where(x => x.Kind == ProviderLedgerKind.ActualSpend)
            .ToListAsync(ct);

        return all
            .GroupBy(x => (x.Provider, x.PeriodFrom, x.PeriodTo))
            .Select(x => x.MaxBy(e => (e.CreatedUtc, e.Id))!)
            .OrderBy(x => x.PeriodFrom)
            .ToArray();
    }

    /// <summary>
    /// Расчёт за промежуток — то, с чем сравнивается факт: стоимость ходов по прайсу,
    /// записанная при их завершении, по всем моделям поставщика, и служебные расходы
    /// (<see cref="ServiceSpend"/>): они ходят к тому же поставщику и попадают в тот же
    /// счёт. Без них остаток у поставщика был бы оптимистом ровно на них.
    /// <paramref name="to"/> = <c>null</c> — до сих пор
    /// </summary>
    public async Task<decimal> Estimate(string provider, DateTime from, DateTime? to, CancellationToken ct = default)
    {
        var runs = await db.RunSpend
            .Where(x => x.Model.Provider == provider
                        && x.CreatedUtc >= from && (to == null || x.CreatedUtc < to))
            .SumAsync(x => x.Cost, ct);

        // Поставщик — своей колонкой, а не через модель: у расшифровки строки
        // справочника нет (13.5)
        var service = await db.ServiceSpend
            .Where(x => x.Provider == provider
                        && x.CreatedUtc >= from && (to == null || x.CreatedUtc < to))
            .SumAsync(x => x.Cost, ct);

        return runs + service;
    }

    /// <summary>
    /// Счёт у поставщика за всё время. Потрачено — факты и расчёт за дни, которые
    /// ни один факт не накрывает: до первого, в промежутках между фактами (есть январь
    /// и март, февраль не введён) и после последнего. Цифра из кабинета запаздывает
    /// и вводится не всегда, а деньги кончаются по-настоящему, и остаток без этих дней
    /// был бы оптимистом — тревога о низком остатке сработала бы поздно
    /// </summary>
    public async Task<AdminProviderAccountDto> Account(
        string provider,
        IReadOnlyCollection<ProviderLedgerEntry> facts,
        CancellationToken ct = default)
    {
        var payments = await db.ProviderLedger
            .AsNoTracking()
            .Where(x => x.Provider == provider && x.Kind == ProviderLedgerKind.Payment)
            .Select(x => x.Amount)
            .ToListAsync(ct);

        var own = facts.Where(x => x.Provider == provider).OrderBy(x => x.PeriodFrom).ToList();

        // Факты одного поставщика не пересекаются (AdminBillingService.Record), но курсор
        // идёт по максимуму конца — пересечение не посчитало бы дни дважды и так
        var unbilled = 0m;
        var cursor = DateTime.MinValue;
        foreach (var fact in own)
        {
            if (fact.PeriodFrom > cursor) unbilled += await Estimate(provider, cursor, fact.PeriodFrom, ct);
            if (fact.PeriodTo > cursor) cursor = fact.PeriodTo!.Value;
        }

        unbilled += await Estimate(provider, cursor, null, ct);

        var paid = payments.Sum();
        var spent = own.Sum(x => x.Amount);

        return new AdminProviderAccountDto
        {
            HasPayments = payments.Count > 0,
            Paid = paid,
            Fact = spent,
            Unbilled = unbilled,
            Remaining = paid - spent - unbilled
        };
    }
}
