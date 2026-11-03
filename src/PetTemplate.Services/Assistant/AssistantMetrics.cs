using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Метрики ассистента (спец. assistant, 13.4). Без текстов, тем же OTLP,
/// с разрезом по <c>Escalated</c> — уложился ход в малый бюджет или перешагнул.
/// Здесь же поиск по справке (спец. help, 10): в справку ходит <c>search_help</c>.
///
/// Синглтон: <see cref="Meter"/> живёт со всем приложением, а инструменты в нём
/// заводятся один раз.
/// </summary>
public class AssistantMetrics : IDisposable
{
    /// <summary>Имя, по которому метрики подключаются в <c>Program</c></summary>
    public const string MeterName = "PetTemplate.Assistant";

    /// <summary>Имя источника спанов: прогон — корневой, вызовы инструментов — дочерние</summary>
    public const string ActivitySourceName = "PetTemplate.Assistant";

    /// <summary>
    /// Имена приборов начинаются с продукта: без префикса они выглядят как коды
    /// отказов словаря `assistant` и попадают в сверку серверных кодов
    /// (<c>npm run i18n:codes</c>), которой до метрик дела нет
    /// </summary>
    private const string Prefix = "pettemplate.assistant.";

    private readonly Meter _meter;
    private readonly Counter<long> _tokensIn;
    private readonly Counter<long> _tokensOut;
    private readonly Histogram<int> _steps;
    private readonly Histogram<double> _runDuration;
    private readonly Histogram<double> _toolDuration;
    private readonly Counter<long> _stopReasons;
    private readonly Counter<long> _planOutcomes;
    private readonly Counter<long> _providerErrors;
    private readonly Counter<long> _breaker;
    private readonly Counter<long> _unavailable;
    private readonly Counter<long> _helpSearches;

    /// <summary>Коммит справки, на которой сейчас ищет API (спец. help, 10)</summary>
    private string? _helpCommit;

    /// <summary>
    /// Последнее увиденное состояние размыкателя по моделям: 1 — разомкнута, 0 — нет.
    /// Обновляется при каждом выборе модели и при каждом переходе размыкателя; само
    /// состояние живёт в базе, здесь — снимок для прибора. Только включённые модели:
    /// выключенная или переименованная из снимка уходит (<see cref="ModelStates"/>,
    /// <see cref="ForgetModel"/>), иначе её последнее «разомкнута» висело бы ложной
    /// тревогой до рестарта
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _open = new();

    public static readonly ActivitySource Source = new(ActivitySourceName);

    public AssistantMetrics() : this(MeterName)
    {
    }

    /// <summary>
    /// Под чужим именем — для мира сверки модели (спец. admin, 15.3) и для тестов: ходы
    /// там выдуманы, и в метрики продукта им хода нет. Приложение подписано только
    /// на <see cref="MeterName"/>, а под другим именем приборы никто не слушает
    /// </summary>
    public AssistantMetrics(string meterName)
    {
        _meter = new Meter(meterName);
        _tokensIn = _meter.CreateCounter<long>(Prefix + "run.tokens.in");
        _tokensOut = _meter.CreateCounter<long>(Prefix + "run.tokens.out");
        _steps = _meter.CreateHistogram<int>(Prefix + "run.steps");
        _runDuration = _meter.CreateHistogram<double>(Prefix + "run.duration", "ms");
        _toolDuration = _meter.CreateHistogram<double>(Prefix + "tool.duration", "ms");
        _stopReasons = _meter.CreateCounter<long>(Prefix + "run.stop_reason");
        _planOutcomes = _meter.CreateCounter<long>(Prefix + "plan.outcome");
        _providerErrors = _meter.CreateCounter<long>(Prefix + "provider.errors");
        _breaker = _meter.CreateCounter<long>(Prefix + "model.breaker");
        _unavailable = _meter.CreateCounter<long>(Prefix + "model.unavailable");
        _helpSearches = _meter.CreateCounter<long>(Prefix + "help.search");
        _meter.CreateObservableGauge(Prefix + "help.index_version", () => Volatile.Read(ref _helpCommit) is { } commit
            ? [new Measurement<int>(1, new KeyValuePair<string, object?>("commit", commit))]
            : Array.Empty<Measurement<int>>());
        _meter.CreateObservableGauge(Prefix + "model.open", () => _open
            .Select(x => new Measurement<int>(x.Value, new KeyValuePair<string, object?>("model", x.Key)))
            .ToArray());
    }

    /// <summary>
    /// Итог хода. Разрез по эскалации — тот самый, по которому настраивается
    /// <c>StepsSoft</c>: доля ходов, перешагнувших малый бюджет
    /// </summary>
    public void Run(AssistantStopReason stop, bool escalated, int steps, long tokensIn, long tokensOut, double ms)
    {
        var tag = new KeyValuePair<string, object?>("escalated", escalated);

        _tokensIn.Add(tokensIn, tag);
        _tokensOut.Add(tokensOut, tag);
        _steps.Record(steps, tag);
        _runDuration.Record(ms, tag);
        _stopReasons.Add(1, tag, new KeyValuePair<string, object?>("reason", stop.ToString()));

        if (stop == AssistantStopReason.ProviderError) _providerErrors.Add(1);
    }

    /// <summary>
    /// Длительность вызова инструмента с его именем: по ней видно, что именно
    /// съедает время длинного хода
    /// </summary>
    public void Tool(string name, double ms) =>
        _toolDuration.Record(ms, new KeyValuePair<string, object?>("tool", name));

    /// <summary>
    /// Поиск по справке (спец. help, 10): <c>found=false</c> — вопрос, на который в справке
    /// ответа нет; их доля — список дыр в справке
    /// </summary>
    public void HelpSearch(bool found) =>
        _helpSearches.Add(1, new KeyValuePair<string, object?>("found", found));

    /// <summary>
    /// Версия справки в памяти: по ней видно, доехала ли публикация, — коммит тот же,
    /// что у выкаченного кода (спец. help, 10)
    /// </summary>
    public void HelpVersion(string commit) => Volatile.Write(ref _helpCommit, commit);

    /// <summary>
    /// Судьба плана. Главная метрика фичи — доля ходов, дошедших до согласованного
    /// плана; рядом — доля пересобранных после «да, но» (прямая мера качества разбора
    /// фразы)
    /// </summary>
    public void Plan(string outcome) =>
        _planOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>
    /// Событие размыкателя (16.2): отказ, размыкание, пробный ход, замыкание.
    /// Размыкание — то, что надо замечать: своя карта, молча отвалившаяся на неделю,
    /// обходится в реальные деньги на покупной модели
    /// </summary>
    public void Breaker(string model, string breakerEvent) =>
        _breaker.Add(1,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("event", breakerEvent));

    /// <summary>Разомкнута ли модель — для прибора состояния</summary>
    public void ModelState(string model, bool open) => _open[model] = open ? 1 : 0;

    /// <summary>
    /// Снимок целиком — по всем включённым моделям, на каждом выборе модели. Чего в нём
    /// нет, то из прибора уходит: модель выключили или переименовали
    /// </summary>
    public void ModelStates(IReadOnlyDictionary<string, bool> enabled)
    {
        foreach (var model in _open.Keys)
            if (!enabled.ContainsKey(model)) _open.TryRemove(model, out _);

        foreach (var (model, open) in enabled) ModelState(model, open);
    }

    /// <summary>Модель больше не ходит — выключена или сменила ключ (экран «Модели», спец. admin, 15.1)</summary>
    public void ForgetModel(string model) => _open.TryRemove(model, out _);

    /// <summary>Все модели разомкнуты или выключены — ходу некуда идти</summary>
    public void Unavailable() => _unavailable.Add(1);

    public void Dispose()
    {
        _meter.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Значения тега <c>outcome</c> у <see cref="AssistantMetrics.Plan"/> (13.4)
/// </summary>
public static class PlanOutcomes
{
    /// <summary>План показан человеку и ждёт «да» — ход дошёл до плана</summary>
    public const string Proposed = "proposed";

    public const string Applied = "applied";

    /// <summary>Человек прочитал план и что-то в нём поправил («да, но»)</summary>
    public const string RebuiltAfterYesBut = "rebuilt_after_yes_but";

    public const string Cancelled = "cancelled";
}

/// <summary>
/// Значения тега <c>event</c> у <see cref="AssistantMetrics.Breaker"/> (16.2)
/// </summary>
public static class BreakerEvents
{
    public const string Failure = "failure";

    public const string Opened = "opened";

    /// <summary>Пауза вышла, и этот инстанс взял право на пробный ход</summary>
    public const string Probe = "probe";

    /// <summary>Пробный ход прошёл — модель снова работает</summary>
    public const string Closed = "closed";

    /// <summary>Замкнули руками из админки, не дожидаясь паузы (спец. admin, 15.1)</summary>
    public const string ClosedByAdmin = "closed_by_admin";
}
