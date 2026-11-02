using Microsoft.Extensions.AI;

namespace PetTemplate.Services.Search;

/// <summary>
/// Что эмбеддер считает — запрос или индексируемый текст (спец. search, 3). Семейство e5
/// обучено с префиксами <c>query: </c> и <c>passage: </c>, и одной настройкой их не добавить:
/// <see cref="SearchEmbedder"/> должен знать, кто перед ним. Знак едет в
/// <see cref="EmbeddingGenerationOptions.AdditionalProperties"/> — интерфейс
/// <c>IEmbeddingGenerator</c> остаётся общим со справкой и ассистентом.
///
/// Вызов без знака уходит как есть, без префикса: так считают те, кому префиксы
/// не нужны вовсе. Опции заводятся на каждый вызов — объект изменяемый, общий
/// экземпляр однажды поправили бы по месту
/// </summary>
public static class EmbeddingPurpose
{
    /// <summary>Ключ в <see cref="EmbeddingGenerationOptions.AdditionalProperties"/></summary>
    public const string Key = "pettemplate.embedding.purpose";

    public const string Query = "query";

    public const string Passage = "passage";

    /// <summary>Строка поиска — префикс запроса</summary>
    public static EmbeddingGenerationOptions ForQuery() => With(Query);

    /// <summary>Чанки сущности для индекса — префикс документа</summary>
    public static EmbeddingGenerationOptions ForPassage() => With(Passage);

    /// <summary>Знак из опций вызова; нет его — <c>null</c></summary>
    public static string? Of(EmbeddingGenerationOptions? options) =>
        options?.AdditionalProperties?.TryGetValue(Key, out var value) == true ? value as string : null;

    private static EmbeddingGenerationOptions With(string purpose) => new()
    {
        AdditionalProperties = new AdditionalPropertiesDictionary { [Key] = purpose }
    };
}
