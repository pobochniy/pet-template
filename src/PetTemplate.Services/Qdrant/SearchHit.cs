using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Qdrant;

/// <summary>Найденный чанк: сама сущность, её кусок и близость</summary>
public class SearchHit
{
    public SearchEntityType Type { get; set; }

    public string EntityId { get; set; } = string.Empty;

    public int Chunk { get; set; }

    public float Score { get; set; }

    public string Text { get; set; } = string.Empty;
}
