using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Hooks;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Трасса прогона (спец. assistant, 13.1): что ушло в модель, какие вызовы с какими
/// аргументами, что вернулось, сколько это заняло.
///
/// Трасса — запись для людей, и только: в контекст модели она не едет, упавший ход
/// по ней не возобновляется. Поэтому её можно усекать свободно — и нужно: результат
/// поиска уместится в контекст, но человек его глазами не прочтёт.
///
/// Пишет сразу, своим <c>SaveChanges</c>: трасса нужна прежде всего у <b>упавшего</b>
/// хода, а он до общего сохранения не доживает. Контекст — общий с ходом, поэтому
/// тем же сохранением уезжают и накопленные токены прогона: упавшая попытка их
/// тоже израсходовала (12.3)
/// </summary>
public class TraceWriter(ApplicationContext context, IOptions<AssistantOptions> options) : IRunTrace
{
    /// <summary>Потолок на кусок трассы — четыре килобайта, больше человек не прочтёт</summary>
    private const int MaxLength = 4 * 1024;

    private const string Cut = "…[усечено]";

    /// <summary>
    /// Чистка по возрасту (13.1). Трасса — запись для людей, и ничего продуктового
    /// на ней не висит: её можно чистить свободно. Сам прогон при этом остаётся —
    /// это короткая строка без куска переписки, на которой держатся идемпотентность
    /// и метрики (12.3), а «показать ход мысли» на старом ходе отвечает
    /// «подробности уже не хранятся»
    /// </summary>
    public async Task<int> CleanupOld(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.AddDays(-options.Value.TraceRetentionDays);

        return await context.AssistantTraceSteps
            .Where(x => x.CreatedAtUtc < threshold)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Шаг пишется и тогда, когда ход обрывают: «Остановить» отменяет токен хода,
    /// а запись шага, который уже случился, от этого не должна теряться. Поэтому
    /// сохранение — без токена хода: вставка одной строки короткая
    /// </summary>
    public async Task Write(RunStep step, CancellationToken ct)
    {
        await context.AssistantTraceSteps.AddAsync(new AssistantTraceStep
        {
            RunId = step.RunId,
            Step = step.Step,
            Kind = step.Kind,
            ToolName = step.ToolName is { Length: > 64 } name ? name[..64] : step.ToolName,
            Input = Trim(step.Input)!,
            Output = Trim(step.Output),
            TokensIn = (int)Math.Min(step.TokensIn, int.MaxValue),
            TokensOut = (int)Math.Min(step.TokensOut, int.MaxValue),
            DurationMs = step.DurationMs,
            Error = Trim(step.Error),
            CreatedAtUtc = DateTime.UtcNow
        }, CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static string? Trim(string? value)
    {
        if (value == null) return null;

        return value.Length <= MaxLength ? value : value[..MaxLength] + Cut;
    }
}
