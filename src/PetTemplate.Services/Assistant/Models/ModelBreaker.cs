using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PetTemplate.Services.Assistant.Models;

/// <summary>
/// Размыкатель моделей (спец. assistant, 16.2): модель, которая перестала отвечать,
/// размыкается сама, и ходы уходят к следующей по порядку.
///
/// Состояние — в строке <see cref="AiModel"/>, а не в памяти: инстансов API может
/// быть несколько. Поэтому и счётчик, и право на пробу берутся атомарным
/// <c>UPDATE</c>, а не чтением и записью — три отказа на трёх инстансах складываются
/// в три, а проба после паузы достаётся одному.
///
/// Пишет только при отказе и при первом успехе после отказов: на удачный ход
/// лишнего <c>UPDATE</c> нет.
///
/// События и состояние моделей уходят в метрики (<see cref="AssistantMetrics"/>, 13.4)
/// там же, где пишется лог: размыкание своей карты, молча висящее неделю, обходится
/// в реальные деньги на покупной модели.
/// </summary>
public class ModelBreaker(
    ApplicationContext context,
    IOptions<ModelBreakerOptions> options,
    ILogger<ModelBreaker> logger,
    AssistantMetrics? metrics = null)
{
    /// <summary>Разомкнута ли модель на момент <paramref name="now"/> — пауза ещё идёт</summary>
    public static bool IsOpen(AiModel model, DateTime now) => model.OpenUntilUtc >= now;

    /// <summary>
    /// Пауза вышла — взять право на пробный ход. Строка на время пробы остаётся
    /// разомкнутой ещё на одну паузу: остальные инстансы её обходят, а если проба
    /// так и не отчитается (упал процесс), модель попробуют снова по истечении.
    /// <c>false</c> — пробу уже взял кто-то другой
    /// </summary>
    public async Task<bool> TryProbe(AiModel model, DateTime now, CancellationToken ct)
    {
        var hold = now.Add(Pause(model));
        var claimed = await context.AiModels
            .Where(x => x.Id == model.Id && x.OpenUntilUtc < now)
            .ExecuteUpdateAsync(x => x.SetProperty(m => m.OpenUntilUtc, (DateTime?)hold), ct);
        if (claimed == 0) return false;

        logger.LogInformation("Model {ModelKey} is probed after pause", model.Key);
        metrics?.Breaker(model.Key, BreakerEvents.Probe);

        return true;
    }

    /// <summary>
    /// Модель ответила. Замыкаем, только если было что замыкать: пробный ход
    /// закрывает паузу целиком, обычный — обнуляет счётчик отказов, если он не ноль.
    /// Разомкнутую тем временем другим инстансом строку обычный ход не трогает:
    /// судьбу паузы решает проба
    /// </summary>
    public async Task Succeeded(ModelPick pick)
    {
        var model = pick.Model;
        if (!pick.Probe && model.FailureCount == 0) return;

        await Safely(model, async () =>
        {
            if (pick.Probe)
            {
                await context.AiModels
                    .Where(x => x.Id == model.Id)
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(m => m.FailureCount, 0)
                        .SetProperty(m => m.OpenUntilUtc, (DateTime?)null)
                        .SetProperty(m => m.OpenForSeconds, 0));

                logger.LogInformation("Model {ModelKey} is closed: probe succeeded", model.Key);
                metrics?.Breaker(model.Key, BreakerEvents.Closed);
                metrics?.ModelState(model.Key, false);
                return;
            }

            await context.AiModels
                .Where(x => x.Id == model.Id && x.OpenUntilUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.FailureCount, 0));
        });
    }

    /// <summary>
    /// Обращение к модели упало. Отказ ли это — решает <see cref="IsFailure"/>;
    /// нет — размыкателю дела нет. Упавшая проба снова размыкает модель на вдвое
    /// большую паузу (до <c>OpenForMax</c>); обычный отказ растит счётчик и на пороге
    /// размыкает — условным <c>UPDATE</c>, чтобы размыкание случилось один раз, а не
    /// столько, сколько инстансов увидели порог
    /// </summary>
    public async Task Failed(ModelPick pick, Exception error, CancellationToken ct)
    {
        if (!IsFailure(error, ct))
        {
            // Проба и без отказа должна чем-то кончиться, иначе модель простоит
            // разомкнутой всю паузу: ответ 4xx — поставщик жив, проба удалась;
            // «Остановить» и прочее — проба ничего не сказала, отпускаем
            if (pick.Probe)
            {
                if (Answered(error)) await Succeeded(pick);
                else await Release(pick);
            }

            return;
        }

        var model = pick.Model;
        var settings = options.Value;
        metrics?.Breaker(model.Key, BreakerEvents.Failure);

        // Запись отказа ход не отменяет и не заменяет его исключение: токен хода
        // здесь не годится — отказ записывается, даже если ход уже обрывают
        await Safely(model, async () =>
        {
            var now = DateTime.UtcNow;

            if (pick.Probe)
            {
                var pause = TimeSpan.FromSeconds(Math.Min(
                    Pause(model).TotalSeconds * 2,
                    settings.OpenForMax.TotalSeconds));

                await context.AiModels
                    .Where(x => x.Id == model.Id)
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(m => m.FailureCount, m => m.FailureCount + 1)
                        .SetProperty(m => m.OpenForSeconds, (int)pause.TotalSeconds)
                        .SetProperty(m => m.OpenUntilUtc, (DateTime?)now.Add(pause)));

                Opened(model, pause, "probe failed");
                return;
            }

            await context.AiModels
                .Where(x => x.Id == model.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.FailureCount, m => m.FailureCount + 1));

            var failures = await context.AiModels
                .Where(x => x.Id == model.Id)
                .Select(x => x.FailureCount)
                .SingleAsync();
            if (failures < settings.FailuresToOpen) return;

            var openFor = settings.OpenFor;
            var opened = await context.AiModels
                .Where(x => x.Id == model.Id && x.OpenUntilUtc == null)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(m => m.OpenForSeconds, (int)openFor.TotalSeconds)
                    .SetProperty(m => m.OpenUntilUtc, (DateTime?)now.Add(openFor)));

            if (opened > 0) Opened(model, openFor, $"{failures} failures in a row");
        });
    }

    /// <summary>
    /// Проба кончилась ничем: ход оборвали до ответа модели или он упал не на ней.
    /// Пауза, взятая на пробу в <see cref="TryProbe"/>, снимается — следующий ход
    /// пробует модель сразу, а не через паузу. Счётчик и длина паузы не трогаются
    /// </summary>
    public async Task Release(ModelPick pick)
    {
        if (!pick.Probe) return;

        var model = pick.Model;
        await Safely(model, async () =>
        {
            await context.AiModels
                .Where(x => x.Id == model.Id && x.OpenUntilUtc != null)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.OpenUntilUtc, (DateTime?)DateTime.UtcNow));

            logger.LogInformation("Model {ModelKey} probe is released without an outcome", model.Key);
        });
    }

    /// <summary>Поставщик ответил кодом — пусть и отказом в нашем запросе: он жив</summary>
    private static bool Answered(Exception error)
    {
        for (var e = error; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case ClientResultException { Status: > 0 }:
                case HttpRequestException { StatusCode: not null }:
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Что считается отказом (16.2): сетевая ошибка, таймаут, 5xx, отказ авторизации —
    /// поставщик не ответил. Плохой ответ модели — пустой, не по делу, без вызова
    /// инструмента — отказом не считается и сюда не попадает вовсе: он не бросает.
    /// Прочие 4xx — это наш запрос, а не их модель, и к запасной с ним идти незачем.
    ///
    /// 408 и 429 — тоже отказ: поставщик жив, но ответа нет, и запасная как раз
    /// выручит. «Остановить» — не отказ: ход оборвали мы
    /// </summary>
    public static bool IsFailure(Exception error, CancellationToken ct)
    {
        for (var e = error; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case OperationCanceledException:
                    return !ct.IsCancellationRequested;
                case ClientResultException { Status: 0 }:
                    // Ответа нет вовсе — сеть; настоящая причина обычно внутри
                    return true;
                case ClientResultException result:
                    return Failing((HttpStatusCode)result.Status);
                case HttpRequestException { StatusCode: { } status }:
                    return Failing(status);
                case HttpRequestException or SocketException or IOException or TimeoutException:
                    return true;
            }
        }

        return false;
    }

    private static bool Failing(HttpStatusCode status) =>
        (int)status >= 500
        || status is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests;

    /// <summary>Текущая пауза модели; у ни разу не размыкавшейся — первая, <c>OpenFor</c></summary>
    private TimeSpan Pause(AiModel model) =>
        model.OpenForSeconds > 0 ? TimeSpan.FromSeconds(model.OpenForSeconds) : options.Value.OpenFor;

    private void Opened(AiModel model, TimeSpan pause, string why)
    {
        logger.LogWarning("Model {ModelKey} is opened for {Pause}: {Why}", model.Key, pause, why);
        metrics?.Breaker(model.Key, BreakerEvents.Opened);
        metrics?.ModelState(model.Key, true);
    }

    /// <summary>
    /// Учёт размыкателя не решает судьбу хода: упала запись — ход кончается так же,
    /// как кончился бы без неё, а поломку видно в логе
    /// </summary>
    private async Task Safely(AiModel model, Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Circuit breaker state of model {ModelKey} was not written", model.Key);
        }
    }
}
