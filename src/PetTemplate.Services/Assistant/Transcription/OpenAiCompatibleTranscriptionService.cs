using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Services.Assistant.Transcription;

/// <summary>
/// Расшифровка через OpenAI-совместимый <c>POST /audio/transcriptions</c>
/// (спец. assistant, 6.2).
///
/// Формат провода выбран, а движок — нет, и это не противоречие: этот же контракт
/// отдают <c>faster-whisper-server</c>, <c>whisper.cpp server</c>, сам OpenAI и шлюзы
/// к российским сервисам. Адрес и ключ — те же, что у поставщика моделей
/// (<c>AssistantOptions.Providers</c>), своя только модель: смена движка — строка
/// в конфигурации.
/// </summary>
public class OpenAiCompatibleTranscriptionService(
    HttpClient http,
    string apiKey,
    string model,
    ILogger<OpenAiCompatibleTranscriptionService> logger) : ITranscriptionService
{
    public bool Available => true;

    public async Task<TranscriptionResult> Transcribe(
        Stream audio,
        string fileName,
        string contentType,
        string language,
        string? prompt,
        CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(audio);
        file.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type)
            ? type
            : new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(model), "model");
        // Язык задаётся явно: автоопределение на коротких репликах («да», «нет, в июне»)
        // менее надёжно (6.1)
        form.Add(new StringContent(language), "language");
        form.Add(new StringContent("json"), "response_format");
        if (!string.IsNullOrWhiteSpace(prompt)) form.Add(new StringContent(prompt), "prompt");

        using var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = form };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Transcription request failed");

            throw new BadRequestException(AssistantErrors.TranscriptionFailed);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Transcription answered {Status}", response.StatusCode);

                throw new BadRequestException(AssistantErrors.TranscriptionFailed);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                return Parse(body);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                logger.LogWarning(e, "Transcription answered with unexpected body");

                throw new BadRequestException(AssistantErrors.TranscriptionFailed);
            }
        }
    }

    /// <summary>
    /// Текст — обязательно; расход — что движок назвал. OpenAI отдаёт <c>usage</c>
    /// либо секундами (<c>whisper-1</c>), либо токенами (<c>gpt-4o-transcribe</c>),
    /// часть серверов — полем <c>duration</c>, остальные — ничем
    /// </summary>
    private static TranscriptionResult Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var text = root.GetProperty("text").GetString()?.Trim() ?? string.Empty;

        double? seconds = root.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number
            ? duration.GetDouble()
            : null;
        long tokensIn = 0, tokensOut = 0;

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("seconds", out var used) && used.ValueKind == JsonValueKind.Number)
                seconds = used.GetDouble();
            if (usage.TryGetProperty("input_tokens", out var input) && input.ValueKind == JsonValueKind.Number)
                tokensIn = input.GetInt64();
            if (usage.TryGetProperty("output_tokens", out var output) && output.ValueKind == JsonValueKind.Number)
                tokensOut = output.GetInt64();
        }

        return new TranscriptionResult(text, seconds, tokensIn, tokensOut);
    }
}
