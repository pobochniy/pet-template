using Microsoft.Extensions.AI;

namespace PetTemplate.Services.Assistant.Check;

/// <summary>
/// Клиент модели, который заодно запоминает, что она ответила (спец. admin, 15.3):
/// какие инструменты позвала и сколько токенов это стоило. Сверке из админки он даёт
/// токены и вызовы фразы
/// </summary>
public class RecordingChatClient(IChatClient inner) : IChatClient
{
    private readonly List<RecordedResponse> _responses = [];

    public IReadOnlyList<RecordedResponse> Responses => _responses;

    /// <summary>Все входящие, как их отдаёт поставщик: кешированные — внутри</summary>
    public long TokensIn { get; private set; }

    public long TokensCached { get; private set; }

    public long TokensOut { get; private set; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await inner.GetResponseAsync(messages, options, cancellationToken);

        TokensIn += response.Usage?.InputTokenCount ?? 0;
        TokensCached += response.Usage?.CachedInputTokenCount ?? 0;
        TokensOut += response.Usage?.OutputTokenCount ?? 0;

        var calls = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<FunctionCallContent>()
            .Select(x => new RecordedCall
            {
                Name = x.Name,
                Arguments = x.Arguments?.ToDictionary(a => a.Key, a => a.Value) ?? []
            })
            .ToList();

        _responses.Add(new RecordedResponse { Text = response.Text, Calls = calls });

        return response;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        inner.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);

    /// <summary>
    /// Внутренний клиент не наш: в админке это синглтон приложения на пару «поставщик,
    /// модель», и закрывать его вместе с миром одной фразы нельзя
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

/// <summary>Один ответ модели: проза и вызовы инструментов</summary>
public class RecordedResponse
{
    public string Text { get; set; } = string.Empty;

    public List<RecordedCall> Calls { get; set; } = [];
}

public class RecordedCall
{
    public string Name { get; set; } = string.Empty;

    public Dictionary<string, object?> Arguments { get; set; } = [];
}
