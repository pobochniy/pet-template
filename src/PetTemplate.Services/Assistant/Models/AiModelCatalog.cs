using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Assistant.Models;

/// <summary>
/// Справочник моделей (спец. assistant, 16.2): какой моделью идёт ход.
///
/// Порядок — это номер, а не признак «по умолчанию»: ход уходит первой включённой
/// и замкнутой модели; разомкнутые обходит размыкатель.
/// </summary>
public class AiModelCatalog(
    ApplicationContext context,
    ModelBreaker breaker,
    ILogger<AiModelCatalog> logger,
    AssistantMetrics? metrics = null)
{
    /// <summary>
    /// Модель на попытку хода. Выбирается один раз на старте попытки и внутри неё
    /// не меняется: иначе в одном учёте расхода смешались бы два прайса, а в разговоре —
    /// две манеры ответа (16.2).
    ///
    /// Разомкнутая модель пропускается; у той, чья пауза вышла, попытка берёт право
    /// на пробный ход — или идёт дальше, если его уже взял другой инстанс.
    /// <c>null</c> — включённых и замкнутых моделей нет, и ходить некуда
    /// </summary>
    public async Task<ModelPick?> Pick(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var enabled = await context.AiModels
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToArrayAsync(ct);

        // Снимок размыкателя для прибора (13.4) — по всем включённым моделям. Один ключ
        // у двух строк (разные поставщики) прибор и так не различает — берётся первая
        metrics?.ModelStates(enabled
            .DistinctBy(x => x.Key)
            .ToDictionary(x => x.Key, x => ModelBreaker.IsOpen(x, now)));

        foreach (var model in enabled)
        {
            if (model.OpenUntilUtc == null) return new ModelPick(model, false);
            if (ModelBreaker.IsOpen(model, now)) continue;
            if (await breaker.TryProbe(model, now, ct)) return new ModelPick(model, true);
        }

        logger.LogWarning("No model is available: all enabled models are open or none is enabled");
        metrics?.Unavailable();

        return null;
    }

    /// <summary>
    /// Есть ли куда идти ходу: включённая модель, которая не разомкнута или у которой
    /// пауза уже вышла и ждёт пробы. Только читает — право на пробу берёт сам ход
    /// </summary>
    public async Task<bool> AnyAvailable(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        return await context.AiModels
            .AnyAsync(x => x.IsEnabled && (x.OpenUntilUtc == null || x.OpenUntilUtc < now), ct);
    }
}
