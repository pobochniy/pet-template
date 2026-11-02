using System.Text.Json;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Сверить модель набором фраз (спец. admin, 15.3). Одна команда на запуск: фразы идут
/// по очереди, и после каждой запуск записывает, сколько сделано, — повтор после падения
/// продолжает с той же фразы, а не с начала и не вторым счётом
/// </summary>
public class ModelCheckCommand
{
    public Guid JobId { get; set; }

    public override string ToString() => JsonSerializer.Serialize(this);
}
