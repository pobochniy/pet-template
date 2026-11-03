using LocalQueue;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Сверить модель набором фраз (спец. admin, 15.3). Одна команда на запуск: фразы идут
/// по очереди, и после каждой запуск записывает, сколько сделано, — повтор после падения
/// продолжает с той же фразы, а не с начала и не вторым счётом
/// </summary>
[CommandType("model-check")]
public class ModelCheckCommand : ICommand
{
    public Guid JobId { get; set; }
}
