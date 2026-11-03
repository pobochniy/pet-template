namespace PetTemplate.Services.Commands;

/// <summary>
/// Команда очереди (спец. infrastructure, 2.2). Ставится только через
/// <c>CommandQueue.Enqueue</c>: тело — JSON одним сериализатором на всех, тип строки —
/// имя из атрибута <c>[CommandType("kebab-case")]</c> пакета <c>AsuTrack.LocalQueue</c>, а не имя класса.
///
/// Имя — то, что лежит в <c>local_queue.command_type</c> и ждёт разбора, может быть,
/// часами: переименование класса или переезд в другое пространство имён строк не теряет.
/// Сменить само имя — только оставив прежнее вторым аргументом атрибута
/// (<c>[CommandType("new-name", "old-name")]</c>): воркер разбирает строки под обоими.
/// Без атрибута пакет пишет имя класса — это ловит <c>QueueRegistrationTests.QueueNamesAreStable</c>
/// </summary>
public interface ICommand;
