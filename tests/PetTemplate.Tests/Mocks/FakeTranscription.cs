using PetTemplate.Services.Assistant.Transcription;

namespace PetTemplate.Tests.Mocks;

/// <summary>
/// Движок расшифровки в тестах (спец. assistant, 6.2): отвечает заранее записанным
/// и запоминает, что его звали. В сеть не ходит
/// </summary>
public class FakeTranscription(TranscriptionResult result) : ITranscriptionService
{
    public FakeTranscription(string text) : this(new TranscriptionResult(text))
    {
    }

    public bool Available { get; init; } = true;

    /// <summary>Подсказки распознавателю — по одной на обращение</summary>
    public List<string?> Prompts { get; } = [];

    public int Calls => Prompts.Count;

    public Task<TranscriptionResult> Transcribe(
        Stream audio,
        string fileName,
        string contentType,
        string language,
        string? prompt,
        CancellationToken ct)
    {
        Prompts.Add(prompt);

        return Task.FromResult(result);
    }
}

/// <summary>
/// Поставщик расшифровки по проводу: запоминает поля формы и заголовок ключа
/// и отвечает заранее записанным телом — так проверяется
/// <see cref="OpenAiCompatibleTranscriptionService"/> целиком, без сети
/// </summary>
public class FakeTranscriptionHandler(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    : HttpMessageHandler
{
    public Dictionary<string, string> Fields { get; } = [];

    public string? Authorization { get; private set; }

    public Uri? Address { get; private set; }

    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        Address = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();

        if (request.Content is MultipartFormDataContent form)
        {
            foreach (var part in form)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty;
                if (part.Headers.ContentDisposition?.FileName == null)
                    Fields[name] = await part.ReadAsStringAsync(cancellationToken);
            }
        }

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
