using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace PetTemplate.Tests.Mocks;

/// <summary>
/// Модель для тестов (спец. assistant, 15.4): заранее записанные ответы, которые
/// отдаются по одному на вызов.
///
/// Без неё цикл проверить нечем: настоящий поставщик отвечает по-разному от раза
/// к разу, а нам нужно, чтобы «модель позвала инструмент и остановилась» значило
/// ровно это. Сеть здесь не участвует вовсе.
/// </summary>
public class FakeChatClient : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new();

    /// <summary>Что цикл отправил модели — по нему видно промпт и историю</summary>
    public List<IList<ChatMessage>> Asked { get; } = [];

    /// <summary>С какими настройками звали — по ним видно, например, формат ответа</summary>
    public List<ChatOptions?> AskedOptions { get; } = [];

    public int Calls => Asked.Count;

    /// <summary>
    /// Что сделать на очередном обращении к модели (номер начинается с единицы).
    /// Нужно там, где проверяется, что цикл замечает изменение снаружи — например,
    /// «Остановить», пришедший посреди хода
    /// </summary>
    public Action<int>? OnAsk { get; set; }

    /// <summary>Ответ текстом — на нём ход заканчивается</summary>
    public FakeChatClient Says(string text)
    {
        _responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

        return this;
    }

    /// <summary>Ответ с вызовом инструмента — на нём ход делает шаг</summary>
    public FakeChatClient Calling(string tool, object? arguments = null, string? callId = null)
    {
        var args = arguments?.GetType()
            .GetProperties()
            .ToDictionary(x => x.Name, x => x.GetValue(arguments)) ?? [];

        var call = new FunctionCallContent(callId ?? Guid.NewGuid().ToString(), tool, args);

        _responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));

        return this;
    }

    /// <summary>
    /// Ответ как есть — проза и вызовы разом. Нужен фикстурам: там записано
    /// то, что модель ответила на самом деле (спец. assistant, 15.4)
    /// </summary>
    public FakeChatClient Responds(string text, IEnumerable<(string Name, IDictionary<string, object?> Arguments)> calls)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(text)) contents.Add(new TextContent(text));

        foreach (var call in calls)
            contents.Add(new FunctionCallContent(Guid.NewGuid().ToString(), call.Name, call.Arguments));

        _responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)));

        return this;
    }

    /// <summary>
    /// Сколько токенов стоил последний записанный ответ — так, как их отдаёт поставщик:
    /// кешированные входят во входящие (спец. assistant, 13.5). Без этого ответ
    /// бесплатен, и учитывать у хода нечего
    /// </summary>
    public FakeChatClient Costing(long input, long output, long cached = 0)
    {
        _responses.Last().Usage = new UsageDetails
        {
            InputTokenCount = input,
            OutputTokenCount = output,
            CachedInputTokenCount = cached
        };

        return this;
    }

    /// <summary>Тот же вызов столько раз подряд — для детектора повторов (раздел 8)</summary>
    public FakeChatClient CallingTimes(int times, string tool, object? arguments = null)
    {
        for (var i = 0; i < times; i++) Calling(tool, arguments, callId: $"call-{i}");

        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Asked.Add(messages.ToList());
        AskedOptions.Add(options);
        OnAsk?.Invoke(Asked.Count);

        // Запас ответов кончился — значит, цикл сделал больше шагов, чем ожидал тест:
        // отвечаем текстом, чтобы он закончился, а не завис
        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, "Готово."));

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Стрима токенов в фиче нет (раздел 14), но контракт клиента его требует
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
