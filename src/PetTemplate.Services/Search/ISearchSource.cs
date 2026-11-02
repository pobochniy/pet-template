using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Search;

/// <summary>
/// Точка расширения поиска (спец. search, 2 и 9): как достать из базы текст сущности
/// одного типа. Раздел проекта, у которого есть текст, заводит свой
/// <see cref="SearchEntityType"/> и регистрирует к нему источник
/// (<c>AddSearchSource&lt;T&gt;()</c>) — нарезка, эмбеддинг, точки в Qdrant, удаление
/// и полный реиндекс остаются общими, в <c>SearchIndexCommandHandler</c>.
///
/// В Lad и ASU это был <c>switch</c> по типу внутри обработчика; в шаблоне сущностей
/// нет, и знание о них приходит регистрацией, а не правкой ядра
/// </summary>
public interface ISearchSource
{
    SearchEntityType Type { get; }

    /// <summary>
    /// Текст сущности для индекса и её пространство. Заголовок — первым (<c># Название</c>):
    /// по нему ищут не реже, чем по телу. Сущности нет — <c>null</c>: обработчик уносит
    /// её точки, удаление приходит той же командой
    /// </summary>
    Task<SearchDocument?> Load(string entityId, CancellationToken ct);

    /// <summary>Идентификаторы всех сущностей типа — для полного реиндекса (спец. search, 5)</summary>
    Task<IReadOnlyList<string>> AllIds(CancellationToken ct);
}
