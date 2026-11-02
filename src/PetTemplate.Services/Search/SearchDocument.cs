namespace PetTemplate.Services.Search;

/// <summary>
/// Что сущность отдаёт в индекс. <see cref="WorkspaceId"/> обязателен: он уезжает
/// в payload точки и в фильтр каждого поиска (спец. search, 6)
/// </summary>
public sealed record SearchDocument(Guid WorkspaceId, string Text);
