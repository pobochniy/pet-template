using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Переиндексировать сущность (спец. search, 4).
///
/// Одна команда на все сущности и на все три случая — создали, поправили, удалили:
/// обработчик всё равно переписывает чанки целиком, а у сущности, которой уже нет,
/// просто удаляет точки. Заводить команду на каждый тип значит копировать один
/// и тот же обработчик.
///
/// Идентификатор строкой: у сущностей шаблона он <c>Guid</c>, но раздел проекта может
/// завести и числовой, а команде важно только донести его до загрузчика
/// (<c>ISearchSource.Load</c>)
/// </summary>
public class SearchIndexCommand : ICommand
{
    public static string QueueName => "search-index";

    public SearchEntityType EntityType { get; set; }

    public string EntityId { get; set; } = string.Empty;
}
