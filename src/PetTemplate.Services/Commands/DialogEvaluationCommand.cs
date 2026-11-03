using LocalQueue;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Оценить прогон судьёй запуска (спец. admin, 16.3). Ставит пачкой «Оценить» в админке
/// (<c>DialogEvaluationService.Start</c>); команда на прогон, а не на пачку: упавшая
/// оценка не тянет за собой уже оплаченных соседей, а запуск отмечает каждую
/// </summary>
[CommandType("dialog-evaluation")]
public class DialogEvaluationCommand : ICommand
{
    public Guid RunId { get; set; }

    /// <summary>Запуск (<c>AdminJob</c>): в нём выбранный судья и счётчики</summary>
    public Guid JobId { get; set; }
}
