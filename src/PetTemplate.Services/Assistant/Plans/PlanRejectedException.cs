namespace PetTemplate.Services.Assistant.Plans;

/// <summary>
/// План не принят: упёрся в предохранитель раздела 8 или ссылка ведёт не туда
/// (спец. assistant, 7.1).
///
/// Свой тип, а не <c>BadRequestException</c> с <c>ErrorMessage</c>: текст этого отказа
/// читает <b>модель</b>, а не человек. Код отказа нужен клиенту, чтобы показать фразу
/// на языке интерфейса; здесь же получателю нужна человеческая фраза с объяснением,
/// что делать дальше, и на экран она попадёт только в пересказе модели.
/// </summary>
public class PlanRejectedException(string message) : Exception(message);
