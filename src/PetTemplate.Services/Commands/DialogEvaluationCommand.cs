using System.Text.Json;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Оценить прогон судьёй запуска (спец. admin, 16.3). Ставит пачкой «Оценить» в админке
/// (<c>DialogEvaluationService.Start</c>); команда на прогон, а не на пачку: упавшая
/// оценка не тянет за собой уже оплаченных соседей, а запуск отмечает каждую
/// </summary>
public class DialogEvaluationCommand
{
    public Guid RunId { get; set; }

    /// <summary>Запуск (<c>AdminJob</c>): в нём выбранный судья и счётчики</summary>
    public Guid JobId { get; set; }

    public override string ToString() => JsonSerializer.Serialize(this);
}
