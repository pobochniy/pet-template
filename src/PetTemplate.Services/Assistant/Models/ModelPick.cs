using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Assistant.Models;

/// <summary>
/// Модель, выбранная на попытку хода. <see cref="Probe"/> — это пробный ход после
/// паузы размыкателя: право на него у попытки, чей условный <c>UPDATE</c> задел строку
/// </summary>
public record ModelPick(AiModel Model, bool Probe);
