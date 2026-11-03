using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Billing;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant.Transcription;

/// <summary>
/// Голос → текст (спец. assistant, 6.1): проверки на входе, расшифровка и её расход.
///
/// Порядок — как у <c>Send</c> (14): сначала то, что видно из самого запроса (пустая,
/// большая, длинная запись — длина по контейнеру, <see cref="AudioDuration"/>), потом есть ли движок, потом лимит пространства (13.6) — до
/// обращения к поставщику, потому что расшифровка тоже идёт в этот лимит. Расход пишется
/// строкой <c>service_spend</c> вида <see cref="ServiceSpendKind.Transcription"/>
/// с пространством и человеком (13.5).
/// </summary>
public class VoiceInputService(
    ApplicationContext db,
    AssistantThreadService threads,
    ITranscriptionService engine,
    BillingMetrics metrics,
    IOptions<AssistantOptions> options)
{
    /// <summary>
    /// Запас к потолку длительности: таймер клиента и длина, которую он насчитал, расходятся
    /// на доли секунды, и запись ровно в минуту не должна получать отказ
    /// </summary>
    private const int SecondsGrace = 2;

    /// <param name="size">Размер записи в байтах — из запроса, до чтения потока</param>
    /// <param name="declaredSeconds">
    /// Длительность, которую намерил клиент. Ей верим только для раннего отказа и для учёта,
    /// когда ни движок, ни контейнер длительность не назвали (<see cref="AudioDuration"/>)
    /// </param>
    public async Task<string> Transcribe(
        Guid userId,
        Guid workspaceId,
        Stream audio,
        long size,
        string fileName,
        string contentType,
        int? declaredSeconds,
        CancellationToken ct)
    {
        var settings = options.Value.Transcription;

        if (size <= 0) throw new BadRequestException(AssistantErrors.TranscriptionFailed);
        if (size > settings.MaxAudioBytes) throw new BadRequestException(AssistantErrors.AudioTooLarge);
        if (declaredSeconds > settings.MaxSeconds + SecondsGrace)
            throw new BadRequestException(AssistantErrors.AudioTooLong);

        // Длительность — по контейнеру, на сервере: потолок не держится на словах клиента.
        // Запись не больше MaxAudioBytes — в памяти до конца расшифровки, на диск не пишется
        using var buffer = new MemoryStream((int)size);
        await audio.CopyToAsync(buffer, ct);
        var measured = AudioDuration.Seconds(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (measured > settings.MaxSeconds + SecondsGrace) throw new BadRequestException(AssistantErrors.AudioTooLong);
        buffer.Position = 0;

        if (!engine.Available) throw new BadRequestException(AssistantErrors.TranscriptionUnavailable);

        await threads.CheckLimit(userId, workspaceId, DateTime.UtcNow, ct);

        var result = await engine.Transcribe(
            buffer,
            fileName,
            contentType,
            settings.Language,
            await threads.MemberNames(workspaceId, ct),
            ct);

        // Секунды — по словам движка: за них он и выставит счёт. Не назвал — по контейнеру;
        // контейнер не узнан — по словам клиента, но не дольше потолка
        var seconds = (int)Math.Ceiling(result.Seconds
                                        ?? measured
                                        ?? Math.Clamp(declaredSeconds ?? 0, 0, settings.MaxSeconds));

        // Вызов был и стоил денег, даже если текст вышел пустым — тишина тоже минута
        var spend = ServiceSpending.Transcription(
            settings, workspaceId, userId, seconds, result.TokensIn, result.TokensOut);
        db.ServiceSpend.Add(spend);
        await db.SaveChangesAsync(CancellationToken.None);
        metrics.Cost(settings.Model, BillingMetrics.Kind(ServiceSpendKind.Transcription), spend.Cost);

        return result.Text;
    }
}
