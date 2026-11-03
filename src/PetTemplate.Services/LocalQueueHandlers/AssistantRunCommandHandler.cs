using LocalQueue.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Commands;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Обработчик хода (спец. assistant, раздел 3).
///
/// Ход исполняется командой, а не в HTTP-запросе: модель отвечает непредсказуемо,
/// и запрос, ждущий её тридцать секунд, упирается в таймаут прокси раньше, чем
/// в собственный. Очередь заодно — естественный ограничитель нагрузки: ходы идут
/// своим воркером очереди в <c>AssistantOptions.Workers</c> циклов
/// (<c>AddCommandQueue</c> в API) и не ждут за реиндексом поиска.
/// </summary>
public class AssistantRunCommandHandler(
    ApplicationContext context,
    AssistantRunService runs,
    AssistantThreadService threads,
    RunCancellation cancellation,
    ICommandLease lease,
    IOptions<AssistantOptions> options,
    ILogger<AssistantRunCommandHandler> logger) : ICommandHandler<AssistantRunCommand>
{
    /// <summary>
    /// Попыток на ход — две. Бесконечного повтора у хода нет: очередь повторяет
    /// «пока не отработает» только то, что обязано отработать, а ход — не обязан.
    /// Каждая попытка — весь платный цикл заново, поэтому попытка считается до обращения
    /// к модели, а предел проверяется до него же: процесс, убитый посреди хода (OOM),
    /// исключения не бросает, и счёт в одном <c>catch</c> пропустил бы его вечно
    /// </summary>
    public const int MaxAttempts = 2;

    /// <summary>
    /// На сколько продлевается аренда команды перед каждым шагом. Ход в тридцать шагов
    /// идёт минуты, и без продления второй цикл подхватил бы команду, пока первый
    /// ещё думает, — два цикла пошли бы по одному прогону
    /// </summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    public async Task Handle(AssistantRunCommand command, CancellationToken ct)
    {
        var run = await context.AssistantRuns.SingleOrDefaultAsync(x => x.Id == command.RunId, ct);
        if (run == null) return;

        if (run.Status != AssistantRunStatus.Running)
        {
            // «Остановить» успело раньше, чем воркер взял команду: цикл крутить
            // не из чего, но сообщение в ленте всё равно обязано появиться,
            // а тред — двинуться к ждущему сообщению (раздел 3)
            if (run is { Status: AssistantRunStatus.Stopped, FinishedAtUtc: null })
            {
                await runs.FinishStopped(run.Id, ct);
                await threads.StartNextWaiting(run.UserId, run.WorkspaceId, ct);
            }

            // Повтор команды на законченном ходе не делает ничего: идемпотентность
            // по RunId (раздел 3)
            return;
        }

        if (run.Attempts >= MaxAttempts)
        {
            // Прошлая попытка не дошла ни до конца, ни до catch: процесс умер посреди хода.
            // Модель не зовём — ещё один платный цикл по ходу, который уже дважды не
            // кончился, не нужен никому; человек получает строку в ленте (12.3)
            logger.LogError("Assistant run {RunId} is given up: {Attempts} attempts did not finish", run.Id,
                run.Attempts);
            await runs.FinishFailed(run.Id, CancellationToken.None);
            await threads.StartNextWaiting(run.UserId, run.WorkspaceId, CancellationToken.None);

            return;
        }

        run.Attempts++;
        await context.SaveChangesAsync(ct);

        using var source = cancellation.Register(run.Id, ct);
        try
        {
            await runs.Execute(run.Id, () => lease.Extend(Lease, source.Token), source.Token);
        }
        catch (OperationCanceledException) when (source.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Ход оборвали кнопкой прямо посреди запроса к модели. Статус уже Stopped,
            // но сообщение в ленту ещё не написано, а пустого сообщения не бывает.
            // Таймаут HTTP-клиента — тоже OperationCanceledException, но токен хода
            // при нём жив: это отказ поставщика, и он идёт в повтор, как любой другой (16.2)
            logger.LogInformation("Assistant run {RunId} was stopped by user", run.Id);
            await Discard(run);
            await runs.FinishStopped(run.Id, CancellationToken.None);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Остановка процесса (выкатка) — не провал попытки: ход начнётся заново
            // со следующим запуском, и платить за это второй попыткой незачем
            await context.AssistantRuns
                .Where(x => x.Id == run.Id && x.Attempts > 0)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.Attempts, r => r.Attempts - 1), CancellationToken.None);

            throw;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogError(e, "Assistant run {RunId} failed on attempt {Attempt}", run.Id, run.Attempts);

            // Попытки исчерпаны — дальше не бросаем: ход уходит в Failed, а человек
            // получает строку в ленте вместо молчания (12.3)
            if (run.Attempts < MaxAttempts) throw;

            await Discard(run);
            await runs.FinishFailed(run.Id, CancellationToken.None);
        }
        finally
        {
            cancellation.Forget(run.Id);
        }

        // Остановка процесса посреди хода — не конец хода: строку очереди возьмёт
        // следующий запуск, и ход начнётся заново (раздел 3)
        if (ct.IsCancellationRequested) return;

        await Compact(run, CancellationToken.None);
        // Очередь треда: сообщение, пришедшее во время хода, ждало именно этого
        await threads.StartNextWaiting(run.UserId, run.WorkspaceId, CancellationToken.None);
    }

    /// <summary>
    /// Оборванная попытка оставила в контексте несохранённое — недописанный шаг, план
    /// из <c>Finish</c>, упавшего посреди: следующее сохранение (конец хода, очередь треда)
    /// унесло бы это в базу. Конец хода перечитывает прогон сам. Из несохранённого остаются
    /// только токены прогона: они уже израсходованы у поставщика и уйдут в учёт (13.5)
    /// </summary>
    private async Task Discard(AssistantRun run)
    {
        var (tokensIn, tokensCached, tokensOut) = (run.TokensIn, run.TokensCached, run.TokensOut);
        context.ChangeTracker.Clear();

        await context.AssistantRuns
            .Where(x => x.Id == run.Id)
            .ExecuteUpdateAsync(x => x
                .SetProperty(r => r.TokensIn, tokensIn)
                .SetProperty(r => r.TokensCached, tokensCached)
                .SetProperty(r => r.TokensOut, tokensOut), CancellationToken.None);
    }

    /// <summary>
    /// Сжатие треда после хода, а не внутри него: иначе один невезучий человек ждёт
    /// лишние секунды за всех (12.4).
    ///
    /// Пока порог не достигнут, компакция не запускается вовсе: в треде из коротких
    /// просьб её не бывает никогда
    /// </summary>
    private async Task Compact(AssistantRun run, CancellationToken ct)
    {
        var thread = context.AssistantMessages
            .Where(x => x.UserId == run.UserId && x.WorkspaceId == run.WorkspaceId);

        var summary = await thread
            .Where(x => x.Role == AssistantMessageRole.Summary)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => (DateTime?)x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        // Порог — доля окна контекста в символах, считаем консервативно: токенизатор
        // у каждого поставщика свой, а символы одинаковы у всех
        var length = await thread
            .Where(x => x.Role != AssistantMessageRole.Summary
                        && (summary == null || x.CreatedAtUtc > summary))
            .SumAsync(x => x.Content.Length, ct);
        if (length <= options.Value.CompactionThreshold) return;

        context.Enqueue(new AssistantCompactionCommand
        {
            UserId = run.UserId,
            WorkspaceId = run.WorkspaceId
        });
        await context.SaveChangesAsync(ct);
    }
}
