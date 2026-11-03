using Microsoft.Extensions.AI;
using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Assistant.Models;

/// <summary>
/// Клиент модели для строки справочника (спец. assistant, 16.2): модель выбирается
/// на ход, а не на всё приложение, поэтому и клиент достаётся под неё.
/// Тесты подменяют его заранее записанной моделью — в сеть они не ходят
/// </summary>
public interface IModelChatClients
{
    IChatClient For(AiModel model);
}
