using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using LocalQueue.Diagnostics;
using LocalQueue.PostgreSql;
using LocalQueue.Storage;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Хранилище воркера очереди поверх пакетного <c>AsuTrack.LocalQueue.PostgreSql</c>
/// (спец. infrastructure, 2.2). Добавляет пакету две вещи, которых у него нет.
///
/// <b>Имя типа команды — своё, а не имя класса.</b> Пакет узнаёт обработчик по
/// <c>Type.FullName</c> и задать другое не даёт, а строки шаблона несут
/// <c>ICommand.QueueName</c> (<c>CommandQueue.Enqueue</c>): переименование класса иначе
/// оставило бы поставленные строки без обработчика. Выборка просит у таблицы оба имени —
/// и устойчивое, и <c>FullName</c> строк, поставленных до перехода, — а взятые строки
/// отдаёт пакету уже под <c>FullName</c>. Сводка для метрик — тоже под <c>FullName</c>:
/// на нём стоят дашборд и тревога «Команда очереди не проходит».
///
/// <b>Предел попыток.</b> Строка, которую брали больше <see cref="CommandQueueCatalog.MaxAttemptsOf"/>
/// раз, — упала ли команда исключением или процесс умер посреди неё (OOM, выкатка), —
/// обработчику уже не отдаётся: она откладывается насовсем (<see cref="Parked"/>) с ошибкой
/// в лог и счётчиком <c>pettemplate.queue.gave_up</c>. Не удаляется — остаётся в таблице
/// для разбора руками и держит тревогу по <c>TryCount</c>
/// </summary>
internal sealed class CommandQueueStorage(
    ILocalCommandsQueueWorkerStorage inner,
    CommandQueueCatalog catalog,
    CommandQueueMetrics metrics,
    ILogger<CommandQueueStorage> logger) : ILocalCommandsQueueWorkerStorage
{
    /// <summary>
    /// Насколько откладывается строка, у которой кончились попытки, — «насовсем».
    /// Вернуть в работу: <c>update local_queue set locked_till_utc = null, try_count = 0 where id = …</c>
    /// </summary>
    public static readonly TimeSpan Parked = TimeSpan.FromDays(365 * 100);

    private ILocalCommandsQueueWorkerStorage Inner => inner;

    public string DataSourceName => inner.DataSourceName;

    public async Task<IEnumerable<CommandRecord>> Prefetch(int count, TimeSpan timeout,
        IReadOnlyCollection<string> commandTypes, CancellationToken ct)
    {
        string[] types = [.. commandTypes, .. commandTypes.Select(catalog.NameOf).OfType<string>()];
        var records = await inner.Prefetch(count, timeout, types.Distinct().ToArray(), ct);

        var taken = new List<CommandRecord>();
        foreach (var record in records)
        {
            var type = catalog.TypeOf(record.CommandType);

            // TryCount уже с этой выборкой: строка, взятая в (max + 1)-й раз, не отдаётся
            if (record.TryCount > catalog.MaxAttemptsOf(type))
            {
                await GiveUp(record, type, ct);
                continue;
            }

            taken.Add(record.CommandType == type ? record : new CommandRecord
            {
                Id = record.Id,
                CommandType = type,
                Data = record.Data,
                CreatedAtUtc = record.CreatedAtUtc,
                LockedTillUtc = record.LockedTillUtc,
                TryCount = record.TryCount
            });
        }

        return taken;
    }

    public Task<bool> Delete(Guid id, int tryCount, CancellationToken ct) => inner.Delete(id, tryCount, ct);

    public async Task<IEnumerable<LocalQueueStorageSummary>> GetSummary(CancellationToken ct)
    {
        var summary = await inner.GetSummary(ct);

        // Строки одного типа под старым и новым именем — одна строка сводки
        return summary
            .GroupBy(x => catalog.TypeOf(x.CommandType))
            .Select(x => new LocalQueueStorageSummary
            {
                CommandType = x.Key,
                Count = x.Sum(s => s.Count),
                MaxTryCount = x.Max(s => s.MaxTryCount)
            })
            .ToArray();
    }

    public Task<bool> MakeInvisibleFor(Guid id, int tryCount, TimeSpan timeout, CancellationToken ct) =>
        inner.MakeInvisibleFor(id, tryCount, timeout, ct);

    // Сводку пакет собирает один раз на равные хранилища — равенство то же, что у пакетного
    public override bool Equals(object? obj) => obj is CommandQueueStorage other && inner.Equals(other.Inner);

    public override int GetHashCode() => inner.GetHashCode();

    private async Task GiveUp(CommandRecord record, string type, CancellationToken ct)
    {
        logger.LogError(
            "Command {Id} of {CommandType} is given up after {TryCount} attempts and parked in the queue table",
            record.Id, type, record.TryCount - 1);
        metrics.GaveUp(type);

        await inner.MakeInvisibleFor(record.Id, record.TryCount, Parked, ct);
    }

    /// <summary>
    /// Пакетное хранилище Postgres. Класс у пакета внутренний, а его регистрация
    /// (<c>UsePostgreSqlStorage</c>) обернуть себя не даёт — поэтому он создаётся напрямую.
    /// Пакет переименует класс — старт упадёт здесь же, а не тихо
    /// </summary>
    public static ILocalCommandsQueueWorkerStorage Postgres(PostgreSqlLocalCommandsQueueWorkerStorageOptions options)
    {
        var type = typeof(PostgreSqlLocalCommandsQueueWorkerStorageOptions).Assembly
            .GetType("LocalQueue.PostgreSql.PostgreSqlLocalCommandsQueueWorkerStorage", throwOnError: true)!;

        return (ILocalCommandsQueueWorkerStorage)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [options],
            culture: null)!;
    }
}

/// <summary>
/// Типы команд приложения: <c>FullName</c> класса, устойчивое имя строки и предел попыток.
/// Один на приложение — его заполняет каждый воркер своими командами, а читают все
/// хранилища: сводка по таблице видит строки и чужих воркеров
/// </summary>
public sealed class CommandQueueCatalog
{
    private readonly ConcurrentDictionary<string, string> _names = new();
    private readonly ConcurrentDictionary<string, string> _types = new();
    private readonly ConcurrentDictionary<string, int> _maxAttempts = new();

    public void Add(string type, string name, int maxAttempts)
    {
        if (!_types.TryAdd(name, type) && _types[name] != type)
            throw new InvalidOperationException($"Command queue name '{name}' is used by {_types[name]} and {type}");

        _names[type] = name;
        _types[type] = type;
        _maxAttempts[type] = Math.Max(maxAttempts, 1);
    }

    /// <summary>Устойчивое имя строки по <c>FullName</c> класса; не наша команда — <c>null</c></summary>
    public string? NameOf(string type) => _names.GetValueOrDefault(type);

    /// <summary><c>FullName</c> класса по имени строки — любому из двух; чужое — как есть</summary>
    public string TypeOf(string commandType) => _types.GetValueOrDefault(commandType, commandType);

    /// <summary>Сколько раз строку можно отдать обработчику; без своего предела — без предела</summary>
    public int MaxAttemptsOf(string type) => _maxAttempts.GetValueOrDefault(type, int.MaxValue);
}

/// <summary>
/// Метрики очереди шаблона поверх метрик пакета: сколько команд брошено после исчерпания
/// попыток (спец. infrastructure, 10). Синглтон: <see cref="Meter"/> живёт с приложением
/// </summary>
public sealed class CommandQueueMetrics : IDisposable
{
    public const string MeterName = "PetTemplate.Queue";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _gaveUp;

    public CommandQueueMetrics()
    {
        _gaveUp = _meter.CreateCounter<long>("pettemplate.queue.gave_up");
    }

    public void GaveUp(string commandType) =>
        _gaveUp.Add(1, new KeyValuePair<string, object?>("commandType", commandType));

    public void Dispose() => _meter.Dispose();
}
