using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Search;

/// <summary>
/// Векторный запрос: что ищем, где и сколько. Пространство — обязательное поле,
/// а не параметр по умолчанию (спец. search, 6)
/// </summary>
public sealed record VectorQuery(Guid WorkspaceId, SearchEntityType Type, string Text, int Limit);
