using Microsoft.Extensions.AI;
using PetTemplate.Services.Assistant.Sections;
using PetTemplate.Services.Help;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant.Check;

/// <summary>
/// Что мир сверки берёт у приложения как есть (спец. admin, 15.3): базу, настройки хода
/// и поиска, эмбеддер, справку и разделы проекта. Всё остальное мир собирает сам
/// </summary>
public sealed class ModelCheckEnvironment
{
    /// <summary>Строка подключения приложения: мир живёт в транзакции на той же базе</summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Настройки хода — бюджет шагов, поля поставщика, — как в бою. Копируются: мир
    /// не должен менять настройки приложения
    /// </summary>
    public required AssistantOptions Assistant { get; init; }

    /// <summary>Настройки эмбеддера — справка мира ищет так же, как боевая</summary>
    public required SearchOptions Search { get; init; }

    /// <summary>Эмбеддер приложения — вектор запроса к справке</summary>
    public required IEmbeddingGenerator<string, Embedding<float>> Embedder { get; init; }

    /// <summary>
    /// Справка мира — та, что сейчас в памяти API. Нет — «справка недоступна», и фраза
    /// проверяет только, что её позвали
    /// </summary>
    public HelpIndex? Help { get; init; }

    /// <summary>Разделы проекта — те же, что у приложения (спец. assistant, 7.1)</summary>
    public IReadOnlyList<IAssistantSection> Sections { get; init; } = [];
}
