using System.Diagnostics.Metrics;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Billing;

/// <summary>
/// Метрики расхода (спец. assistant, 13.5–13.6): сводка в админке отвечает «сколько
/// и куда», метрики — «что сломалось». Тем же OTLP, что и метрики ассистента, без текстов.
///
/// Два сорта приборов. Счётчики пишутся по месту — расход хода и служебного вызова,
/// проверка лимита на <c>Send</c>. Состояние (остаток и сверка у поставщика) дорого
/// считать на каждый сбор, и его раз в <c>MonitorInterval</c> снимает
/// <see cref="BillingMonitor"/>; приборы отдают последний снимок.
///
/// Метки — модель, вид и поставщик: конечные наборы. Пространства в метке нет
/// (<c>src/PetTemplate.Api/agents.md</c>, «Метрики и трассы») — кто съел лимит,
/// видно в админке, а не в Prometheus.
///
/// Синглтон: <see cref="Meter"/> живёт со всем приложением
/// </summary>
public class BillingMetrics : IDisposable
{
    /// <summary>Имя, по которому метрики подключаются в <c>Program</c></summary>
    public const string MeterName = "PetTemplate.Billing";

    /// <summary>Значение метки <c>kind</c> у расхода хода; у служебного — вид в нижнем регистре</summary>
    public const string RunKind = "run";

    /// <summary>Префикс продукта — по той же причине, что у <c>AssistantMetrics</c></summary>
    private const string Prefix = "pettemplate.billing.";

    private readonly Meter _meter;
    private readonly Counter<double> _cost;
    private readonly Counter<long> _send;

    private volatile BillingSnapshot? _snapshot;

    public BillingMetrics() : this(MeterName)
    {
    }

    /// <summary>
    /// Под чужим именем — для мира сверки модели (спец. admin, 15.3) и для тестов: ходы
    /// там выдуманы, и в метрики продукта им хода нет
    /// </summary>
    public BillingMetrics(string meterName)
    {
        _meter = new Meter(meterName);
        _cost = _meter.CreateCounter<double>(Prefix + "cost");
        _send = _meter.CreateCounter<long>(Prefix + "send");

        _meter.CreateObservableGauge(Prefix + "provider.balance", () => Snap(s => s.Providers
            .Where(x => x.Remaining != null)
            .Select(x => new Measurement<double>((double)x.Remaining!.Value, Tag("provider", x.Provider)))));
        _meter.CreateObservableGauge(Prefix + "provider.balance.low", () => Snap(s => s.Providers
            .Where(x => x.Remaining != null)
            .Select(x => new Measurement<int>(x.Low ? 1 : 0, Tag("provider", x.Provider)))));
        _meter.CreateObservableGauge(Prefix + "discrepancy", () => Snap(s => s.Providers
            .Where(x => x.Discrepancy != null)
            .Select(x => new Measurement<double>(x.Discrepancy!.Value, Tag("provider", x.Provider)))));
    }

    /// <summary>
    /// Стоимость обращения к модели по прайсу — хода или служебного вызова, по модели:
    /// по ней видно, во что обходится ассистент в час, раньше, чем придёт счёт
    /// </summary>
    public void Cost(string model, string kind, decimal cost) =>
        _cost.Add((double)cost, Tag("model", model), Tag("kind", kind));

    /// <summary>Значение метки <c>kind</c> служебного расхода</summary>
    public static string Kind(ServiceSpendKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>
    /// Проверка лимита на <c>Send</c> (13.6): доля отказов — сигнал, что умолчание
    /// мало или кто-то съедает счёт
    /// </summary>
    public void Send(bool refused) =>
        _send.Add(1, Tag("outcome", refused ? SendOutcomes.LimitReached : SendOutcomes.Allowed));

    public void Snapshot(BillingSnapshot snapshot) => _snapshot = snapshot;

    public void Dispose()
    {
        _meter.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>До первого снимка приборы молчат, а не показывают нули</summary>
    private IEnumerable<Measurement<T>> Snap<T>(Func<BillingSnapshot, IEnumerable<Measurement<T>>> read)
        where T : struct =>
        _snapshot is { } snapshot ? read(snapshot).ToArray() : [];

    private static KeyValuePair<string, object?> Tag(string name, object? value) => new(name, value);
}

/// <summary>Значения тега <c>outcome</c> у <see cref="BillingMetrics.Send"/></summary>
public static class SendOutcomes
{
    public const string Allowed = "allowed";

    public const string LimitReached = "limit_reached";
}

/// <summary>Снимок денежного состояния — по поставщикам</summary>
public record BillingSnapshot(IReadOnlyList<ProviderSnapshot> Providers);

/// <param name="Remaining">Остаток счёта; <c>null</c> — платежей поставщику не записано</param>
/// <param name="Low">Остаток ниже <c>ProviderBalanceAlarm</c></param>
/// <param name="Discrepancy">
/// Расхождение «расчёт / факт» по последнему факту, долей от факта: −0,006 — это −0,6 %.
/// <c>null</c> — факта нет или он нулевой
/// </param>
public record ProviderSnapshot(string Provider, decimal? Remaining, bool Low, double? Discrepancy);
