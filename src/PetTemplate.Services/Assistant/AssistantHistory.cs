using Microsoft.Extensions.AI;
using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Что из треда едет в ход (спец. assistant, 12.4): сама переписка — сообщениями,
/// резюме и операции незакрытого плана — блоком данных хода (спец. assistant, 9)
/// </summary>
public class AssistantHistory
{
    public List<ChatMessage> Messages { get; set; } = [];

    public string? Summary { get; set; }

    public AssistantPlan? PendingPlan { get; set; }
}
