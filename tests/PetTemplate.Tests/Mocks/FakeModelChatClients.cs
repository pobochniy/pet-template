using Microsoft.Extensions.AI;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Models;

namespace PetTemplate.Tests.Mocks;

/// <summary>
/// Клиенты моделей для тестов: какую бы строку справочника ни выбрал ход,
/// отвечает одна и та же записанная модель. Кого спросили — видно по <see cref="Asked"/>
/// </summary>
public class FakeModelChatClients(IChatClient chat) : IModelChatClients
{
    public List<AiModel> Asked { get; } = [];

    public IChatClient For(AiModel model)
    {
        Asked.Add(model);

        return chat;
    }
}
