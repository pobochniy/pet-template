using PetTemplate.Services.Commands;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Команда на переиндексацию — по сущности (спец. search, 4). Раздел ставит её
/// <c>db.Enqueue(SearchIndex.Of(...))</c> тем же сохранением, что и саму правку (outbox).
///
/// Фабрика, а не <c>new</c> по месту: идентификатор в команде строковый,
/// и каждый вызывающий иначе сам решал бы, как превратить в строку свой ключ
/// </summary>
public static class SearchIndex
{
    public static SearchIndexCommand Of(SearchEntityType type, Guid entityId) => Of(type, entityId.ToString());

    public static SearchIndexCommand Of(SearchEntityType type, string entityId) => new()
    {
        EntityType = type,
        EntityId = entityId
    };
}
