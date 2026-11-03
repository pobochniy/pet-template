using LocalQueue;
using LocalQueue.PostgreSql;
using LocalQueue.Processing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PetTemplate.Services.Commands;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Очередь команд на пакете <c>AsuTrack.LocalQueue.PostgreSql</c> (спец. infrastructure, 2.2;
/// спец. mail, 4). Постановка — своя, <c>CommandQueue.Enqueue</c> тем же <c>SaveChanges</c>,
/// что и доменное изменение (outbox): пакетный <c>ICommandsQueue</c> просит отдельные
/// соединение и транзакцию. Тело строки — JSON по умолчанию, как у пакета, а тип — устойчивое
/// имя из атрибута пакета <c>[CommandType]</c>, а не имя класса. Предел попыток — тоже пакетный
/// (<see cref="BackoffRetryPolicy.MaxAttempts"/>).
///
/// Разбор — воркерами пакета. Воркер — свой набор типов команд со своими циклами,
/// пачкой выборки и арендой: долгие команды (ход ассистента, сверка модели) идут своими
/// воркерами и не стоят за реиндексом поиска, а письма не стоят за ними. Очередь заводится
/// один раз (<see cref="AddCommandQueueStorage"/>), воркеры — сколько нужно (<see cref="AddCommandQueueWorker"/>)
/// </summary>
public static class CommandQueueRegistration
{
    /// <summary>Таблица очереди — <c>Db.Entity.LocalQueue</c>, колонки — как у пакета</summary>
    public const string TableName = "local_queue";

    /// <summary>Первый повтор упавшей команды по умолчанию — через минуту, дальше пауза удваивается</summary>
    private static readonly TimeSpan DefaultRetryAfterFailure = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Строка подключения и настройки воркеров — один раз на приложение. Пакет фиксирует
    /// строку подключения при регистрации воркера, а воркеры добавляются и после
    /// (тесты — своими типами команд), поэтому она лежит в контейнере готовой
    /// </summary>
    public static IServiceCollection AddCommandQueueStorage(this IServiceCollection services, IConfiguration config)
    {
        services.TryAddSingleton(new CommandQueueSettings(
            config.GetConnectionString("AppConnection")!,
            config.GetSection("CommandQueueOptions").Get<CommandQueueOptions>() ?? new CommandQueueOptions()));

        return services;
    }

    /// <summary>
    /// Воркер очереди: свои типы команд (<paramref name="handlers"/>), <paramref name="workers"/>
    /// циклов разбора, пачка выборки и аренда. Без явных значений — общий воркер
    /// (<see cref="CommandQueueOptions"/>, один цикл: команды идут по одной).
    /// </summary>
    /// <param name="prefetchCount">
    /// Сколько строк берёт выборка. У долгих команд — одна: взятая пачка ждала бы,
    /// пока освободятся циклы, держа аренду
    /// </param>
    /// <param name="invisibilityTimeout">
    /// Аренда выборки — заведомо больше одного шага долгого обработчика; дальше её
    /// продлевает он сам (<see cref="ICommandLease"/>)
    /// </param>
    public static IServiceCollection AddCommandQueueWorker(
        this IServiceCollection services,
        Action<CommandHandlers> handlers,
        int workers = 1,
        int? prefetchCount = null,
        TimeSpan? invisibilityTimeout = null)
    {
        // Только не keyed-регистрации: у keyed ImplementationInstance бросает исключение,
        // и любой AddKeyed* до очереди ронял бы старт
        var settings = services
            .Where(x => !x.IsKeyedService && x.ServiceType == typeof(CommandQueueSettings))
            .Select(x => x.ImplementationInstance)
            .OfType<CommandQueueSettings>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Call AddCommandQueueStorage before AddCommandQueueWorker");

        if (!settings.Options.Enabled)
        {
            // Разбора нет, а обработчики остаются: их зовут напрямую тесты и соседние сервисы
            services.TryAddScoped<ICommandLease, DetachedCommandLease>();
            handlers(new CommandHandlers(services, null, settings.Options));

            return services;
        }

        return services.AddLocalCommandQueueWorker(c =>
        {
            c.UsePostgreSqlStorage(new PostgreSqlLocalCommandsQueueWorkerStorageOptions
            {
                ConnectionString = settings.ConnectionString,
                TableName = TableName
            });
            c.WorkersCount = Math.Max(workers, 1);
            c.PrefetchCount = Math.Max(prefetchCount ?? settings.Options.PrefetchCount, 1);
            c.InvisibilityTimeout = invisibilityTimeout ?? settings.Options.InvisibilityTimeout;
            c.IdleTimeout = settings.Options.PollInterval;

            handlers(new CommandHandlers(services, c, settings.Options));
        });
    }

    private sealed record CommandQueueSettings(string ConnectionString, CommandQueueOptions Options);

    /// <summary>Обработчики одного воркера</summary>
    public sealed class CommandHandlers(
        IServiceCollection services,
        LocalCommandQueueWorkerConfigurator? worker,
        CommandQueueOptions options)
    {
        /// <summary>
        /// Обработчик <typeparamref name="THandler"/> команды <typeparamref name="TCommand"/>.
        /// Одна попытка на выдачу; упала — строка прячется на <paramref name="retryAfterFailure"/>
        /// (по умолчанию минута), и с каждой следующей попыткой пауза удваивается до
        /// <see cref="CommandQueueOptions.MaxRetryAfterFailure"/>. После <paramref name="maxAttempts"/>
        /// выдач (по умолчанию <see cref="CommandQueueOptions.MaxAttempts"/>) пакет бросает команду:
        /// ошибка в лог, счётчик <c>local-command-queue-given-up</c>, строка остаётся в таблице
        /// запертой насовсем. Повтор, но не вечный
        /// </summary>
        public CommandHandlers Add<TCommand, THandler>(TimeSpan? retryAfterFailure = null, int? maxAttempts = null)
            where TCommand : ICommand
            where THandler : class, ICommandHandler<TCommand>
        {
            // Пакет берёт обработчик из области команды — регистрация за приложением
            services.TryAddTransient<THandler>();
            worker?.AddCommandHandler<TCommand, THandler>(new BackoffRetryPolicy(
                retryAfterFailure ?? DefaultRetryAfterFailure,
                options.MaxRetryAfterFailure,
                maxAttempts ?? options.MaxAttempts));

            return this;
        }
    }

    /// <summary>
    /// Одна попытка на выдачу, повтор — через <paramref name="first"/>, затем вдвое дольше
    /// с каждой попыткой (<c>TryCount</c> строки переживает рестарт), но не дольше
    /// <paramref name="max"/>. Команда, которая не пройдёт никогда, не занимает воркер
    /// и провайдера раз в минуту, а растущий <c>TryCount</c> поднимает тревогу.
    /// <c>null</c> («не откладывать») политика не отдаёт: строка тогда вернулась бы, как
    /// только кончится аренда выборки. Конец повторам — <see cref="MaxAttempts"/>: пакет считает
    /// выдачи, поэтому ловит и процесс, умерший посреди команды
    /// </summary>
    public sealed class BackoffRetryPolicy(TimeSpan first, TimeSpan max, int maxAttempts) : ICommandRetryPolicy
    {
        /// <summary>Дальше удваивать незачем: 2^20 минут — уже годы, потолок сработает раньше</summary>
        private const int MaxDoublings = 20;

        public int MaxRetryCount => 1;

        public int? MaxAttempts => Math.Max(maxAttempts, 1);

        public TimeSpan BackoffFor(int retry) => TimeSpan.Zero;

        public TimeSpan? RedeliverAfter(int attempt)
        {
            var doublings = Math.Clamp(attempt - 1, 0, MaxDoublings);
            var delay = first * Math.Pow(2, doublings);

            return delay < max ? delay : TimeSpan.FromTicks(Math.Max(max.Ticks, first.Ticks));
        }
    }
}
