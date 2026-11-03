namespace PetTemplate.Shared.Exceptions;

/// <summary>
/// Отказ, задуманный кодом, — корень своих исключений. <c>GlobalExceptionHandler</c> отвечает
/// статусом по наследнику (<see cref="BadRequestException"/> и <see cref="AdminRejectionException"/> —
/// 400, <see cref="NotFoundException"/> — 404, <see cref="ConflictException"/> — 409) и текстом
/// исключения. Всё, что не отсюда (<see cref="ArgumentException"/>,
/// <see cref="InvalidOperationException"/> из библиотек и <c>SingleAsync</c>), — баг: 500 без
/// подробностей
/// </summary>
public abstract class DomainException(string message) : Exception(message);
