# Очередь команд — своя, пока у `LocalQueue` нет хранилища под PostgreSQL

## Что сейчас

Письма (спец. `mail.md`, 4) уходят через очередь команд: команда
`SendMailCommand` ложится в таблицу `local_queue` тем же `SaveChanges`, что и токен
(outbox), а разбирает её обработчик `SendMailCommandHandler`. Но разбирает не пакет
`localqueue`, а свой воркер `CommandQueueWorker` (`src/PetTemplate.Api/Workers`).

Почему (разбор `~/proj/LocalQueue`):

- Ядро `localqueue` без хранилища не работает: `AddLocalCommandQueueWorker` без `UseStorage`
  бросает `No storage configured`, хранилища в памяти в пакете нет — только интерфейсы
  `ILocalCommandsQueueStorage` / `ILocalCommandsQueueWorkerStorage`.
- Хранилище есть только `LocalQueue.MySql` (тянет `MySqlConnector`), база шаблона — PostgreSQL.
- `LocalQueue.PostgreSql` в исходниках очереди — черновик: код закомментирован, ссылки
  на Npgsql нет, не публикуется.

Свой воркер устроен так, чтобы замена на пакет прошла без правок вокруг:

- таблица — те же колонки, что у пакета (`id`, `data`, `command_type`, `created_at_utc`,
  `locked_till_utc`, `try_count`), `id` — родной `uuid`;
- постановка — `CommandQueue.Enqueue` через `DbContext` (мимо API пакета);
- обработчик — `ICommandHandler<T>` с той же сигнатурой, что `LocalQueue.Processing.ICommandHandler<T>`;
- выборка — `UPDATE … FOR UPDATE SKIP LOCKED`, упавшая команда откладывается на минуту
  и повторяется без предела попыток, как у пакета.

Письма при перезапуске **не теряются**: строки лежат в базе, недоделанную возьмёт
следующий запуск после истечения блокировки (`CommandQueueOptions.InvisibilityTimeout`).

## Что своя очередь умеет сверх пакета

Ассистенту (`specs/assistant.md`, раздел 3) понадобились две вещи, которых у пакета 1.0.4 нет —
на нём они обходились бы хаками:

- **Дорожки** (`CommandQueueLane`, `AddCommandQueueLane`, `AddCommandHandler<…>(lane: …)`):
  свой набор типов команд, своё число циклов разбора, своя выборка и аренда. Выборка и так
  фильтрует строки по типу (`command_type = ANY(…)`), поэтому дорожки над одной таблицей
  друг другу не мешают. Ход и компакция ассистента — дорожка `assistant`:
  `AssistantOptions:Workers` циклов, выборка по одной строке, аренда 10 минут. У `Prefetch` пакета
  фильтра по типу нет: ход жил бы в общем пуле, увеличивая `WorkersCount` всем.
- **Продление аренды** своей строки обработчиком (`ICommandLease.Extend`, реализация
  `CommandLease` — воркер называет ей строку перед вызовом обработчика). На пакете обработчику
  пришлось бы искать свою строку по типу и подстроке тела команды и править `LockedTillUtc`
  в обход пакета.

Проверяет `Features/Queue/QueueLanesWorker`.

**Реиндекс и ассистент.** Полный реиндекс поиска (`SearchReindexCommand` и сотни
`SearchIndexCommand`) идёт общей дорожкой одним циклом — ход ассистента за ним **не ждёт**:
у него своя дорожка. Ждут за реиндексом письма — они на той же общей дорожке (см. «Когда
возвращаться»). Обратного тоже нет: ход на минуты не держит ни письма, ни индексацию.

## Чего нет по сравнению с пакетом

- **Пакета `LocalQueue.OpenTelemetry`.** Метрики очереди свои (пришли с телеметрией):
  `CommandQueueMetrics` и сводка по таблице раз в 15 секунд в том же воркере — те же имена
  инструментов (`local-command-queue-length`, `-max-try-count`, `-completed`,
  `-processing-time`, `-processing-lag`), метка `commandType`, единицы и границы гистограмм,
  что у пакета; подключаются `AddCommandQueueInstrumentation()` вместо
  `AddDodoLocalCommandQueueInstrumentation()`. Дашборд «Очередь команд», собранный под
  метрики пакета, работает на них без переделки.
- **Параллельной обработки внутри общей дорожки**: её команды идут по одной. Для писем
  хватает; долгим командам — своя дорожка с несколькими циклами.
- **Политик повтора** сложнее «через N минут» и отложенной постановки (`processingStartTime`).

## Что сделать

1. В `github.com/pobochniy/localqueue` дописать `LocalQueue.PostgreSql`: хранилище команд
   на Npgsql (`Create`, `Prefetch` с `FOR UPDATE SKIP LOCKED`, `Delete`, `MakeInvisibleFor`,
   `GetSummary`) над таблицей с колонками выше и `uuid` в `id`; опубликовать в `pobhub`.
2. В шаблоне: `localqueue`, `LocalQueue.PostgreSql`, `LocalQueue.OpenTelemetry` в
   `Directory.Packages.props`; регистрация `AddLocalCommandQueueWorker` с `UseStorage`
   вместо `AddCommandQueueWorker`; в обработчиках `using LocalQueue.Processing`; удалить
   `CommandQueueWorker`, `CommandQueueRegistration`, `CommandHandlerRegistration`,
   `CommandQueueOptions`, `CommandQueueMetrics`, свой `ICommandHandler`; в `Program.cs` —
   `AddDodoLocalCommandQueueInstrumentation()` вместо `AddCommandQueueInstrumentation()`
   (имена метрик те же — дашборд и графики не прервутся). Таблица и `CommandQueue.Enqueue`
   остаются.
   Дорожки и аренда — **условие замены**: пакет должен уметь выборку по набору типов
   (второй воркер на дорожку ассистента) и продление аренды из обработчика; без этого
   ход ассистента вернётся в общий пул и снова начнёт ждать за реиндексом.
3. Restore начнёт ходить в `pobhub` — нужен `NUGET_GITHUB_TOKEN` (README, «Пакеты из
   закрытого фида»; `nuget.config` уже настроен).

## Когда возвращаться

Ассистенту хватило дорожек и аренды (выше). Индексация поиска (`specs/search.md`) идёт общей
дорожкой — команда на сущность, по одной: полный реиндекс большой базы разбирается долго
и на это время держит общую дорожку, **письма ждут за ним**. Возвращаться — когда это
станет заметно (подтверждение почты ждут минуты): индексацию — на свою дорожку тем же
`lane:`, без переделки очереди.
