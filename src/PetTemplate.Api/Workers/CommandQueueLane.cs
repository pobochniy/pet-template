namespace PetTemplate.Api.Workers;

/// <summary>
/// Дорожка очереди команд (<c>tech-debt/localqueue-postgres.md</c>): свой набор типов
/// команд, свои циклы разбора и своя аренда. Таблица одна, но выборка фильтрует строки
/// по типу (<c>command_type = ANY(…)</c>), и дорожки друг друга не ждут: ход ассистента
/// на минуты не держит письма, а полный реиндекс поиска — ход ассистента.
///
/// Обычные команды — дорожка <see cref="CommandQueueLanes.Default"/> с одним циклом
/// и настройками <see cref="CommandQueueOptions"/>, как было до дорожек
/// </summary>
/// <param name="Name">Имя дорожки — по нему её находят регистрации обработчиков</param>
/// <param name="Workers">Сколько циклов разбора одновременно — столько команд дорожки идут параллельно</param>
/// <param name="PrefetchCount">
/// Сколько строк цикл берёт за одну выборку. У долгих команд — одна: взятая пачка
/// разбирается по очереди, и пятая строка пачки ждала бы четыре хода, держа аренду
/// </param>
/// <param name="InvisibilityTimeout">
/// Аренда выборки — заведомо больше одного шага обработчика; дальше её продлевает
/// он сам (<c>ICommandLease</c>)
/// </param>
public sealed record CommandQueueLane(string Name, int Workers, int PrefetchCount, TimeSpan InvisibilityTimeout);
