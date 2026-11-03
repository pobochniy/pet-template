namespace PetTemplate.Shared.Exceptions;

/// <summary>
/// Отказ админки по содержанию запроса — 400 с русским текстом без кода (спец. admin, 3.5):
/// переводить коды админке нечем, и ошибка показывается как есть. Только для админских
/// сервисов; продукт отказывает <see cref="BadRequestException"/> с кодом
/// </summary>
public class AdminRejectionException(string message) : DomainException(message);
