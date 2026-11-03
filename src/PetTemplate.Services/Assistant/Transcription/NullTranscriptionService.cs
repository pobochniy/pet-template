using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Services.Assistant.Transcription;

/// <summary>
/// Расшифровки нет: модель распознавания не настроена (пустой <c>Transcription.Model</c>)
/// или у её поставщика нет адреса.
///
/// Отвечает внятным отказом, а не тишиной и не пятисоткой: голос — способ заполнить
/// поле ввода, и человек должен понять, что набрать придётся руками (6.3)
/// </summary>
public class NullTranscriptionService : ITranscriptionService
{
    public bool Available => false;

    public Task<TranscriptionResult> Transcribe(
        Stream audio,
        string fileName,
        string contentType,
        string language,
        string? prompt,
        CancellationToken ct) =>
        throw new BadRequestException(AssistantErrors.TranscriptionUnavailable);
}
