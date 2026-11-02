namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Аренда строки очереди, которую сейчас разбирает обработчик (<c>tech-debt/localqueue-postgres.md</c>).
///
/// Обычной команде хватает аренды выборки (<c>InvisibilityTimeout</c> её дорожки).
/// Долгой — ход ассистента в тридцать шагов на медленной модели (спец. assistant, раздел 3) —
/// нет: истекла аренда, и строку взял бы второй цикл, пока первый ещё думает, — два хода
/// пошли бы по одному прогону. Такой обработчик продлевает аренду между шагами.
///
/// Вне воркера (обработчик позвали напрямую, в тесте) строки нет, и продлевать нечего —
/// вызов ничего не делает
/// </summary>
public interface ICommandLease
{
    /// <summary>Не отдавать строку другим ещё <paramref name="duration"/> от текущего момента</summary>
    Task Extend(TimeSpan duration, CancellationToken ct);
}
