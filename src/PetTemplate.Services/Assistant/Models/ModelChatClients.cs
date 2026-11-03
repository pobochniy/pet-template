using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant.Models;

/// <summary>
/// Один <see cref="IChatClient"/> на пару «поставщик, модель» — совместимых с OpenAI
/// сегодня почти весь рынок (спец. assistant, раздел 16): адрес и ключ берутся из секции
/// поставщика в <see cref="AssistantOptions.Providers"/>, имя модели — из справочника.
///
/// <c>UseFunctionInvocation()</c> в цепочке намеренно нет: цикл «модель →
/// инструменты → модель» у нас свой (15.2). Внутрь него встраиваются шаги, проверка
/// «Остановить», продление аренды команды, эскалация бюджета и детектор повторов —
/// всё то, что <c>FunctionInvokingChatClient</c> прячет.
///
/// Синглтон: клиенты держат соединения и строятся один раз на пару.
/// </summary>
public class ModelChatClients(IServiceProvider services, IOptions<AssistantOptions> settings) : IModelChatClients
{
    /// <summary>Заглушки на случай ненастроенного поставщика — см. <see cref="Build"/></summary>
    private const string StubEndpoint = "http://localhost/assistant-not-configured/v1";

    private const string StubApiKey = "not-configured";

    private readonly ConcurrentDictionary<(string Provider, string Key), IChatClient> _clients = new();

    public IChatClient For(AiModel model) =>
        _clients.GetOrAdd((model.Provider.ToLowerInvariant(), model.Key), x => Build(x.Provider, x.Key));

    private IChatClient Build(string provider, string key)
    {
        var options = settings.Value;
        var section = options.Providers.GetValueOrDefault(provider) ?? new AssistantProviderOptions();

        // Секции поставщика нет — не повод ронять ни старт, ни выбор модели: запрос
        // уйдёт в заглушку, и ход кончится отказом поставщика, как при любом другом
        // неответе. Заглушки нужны потому, что и адрес, и ключ клиент проверяет
        // на пустоту прямо в конструкторе
        var endpoint = string.IsNullOrWhiteSpace(section.Endpoint) ? StubEndpoint : section.Endpoint;
        var apiKey = string.IsNullOrWhiteSpace(section.ApiKey) ? StubApiKey : section.ApiKey;

        // Повторы и таймаут SDK — явно (17): по умолчанию клиент сам повторяет запрос трижды
        // по 100 секунд, и это множилось бы с попытками хода незаметно. Повтор запроса
        // гасит мелкий сбой без повтора всего хода; размыкатель видит один отказ — после
        // последнего повтора
        var client = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            RetryPolicy = new ClientRetryPolicy(Math.Max(options.ModelRetries, 0)),
            NetworkTimeout = TimeSpan.FromSeconds(Math.Max(options.ModelTimeoutSeconds, 1))
        };

        return new OpenAIClient(new ApiKeyCredential(apiKey), client)
            .GetChatClient(key)
            .AsIChatClient()
            .AsBuilder()
            // Тексты сообщений — за отдельным выключателем и по умолчанию
            // выключены: в телеметрию уходят структура и числа. Включать его можно,
            // только когда приёмник стоит в том же контуре, что и база: внешний SaaS
            // для трасс — это пересылка переписки третьей стороне. Источник трасс
            // обёртки (Experimental.Microsoft.Extensions.AI) подключён в Program.cs (13.3)
            .UseOpenTelemetry(configure: o => o.EnableSensitiveData = options.TelemetrySensitiveData)
            .UseLogging()
            .Build(services);
    }
}
