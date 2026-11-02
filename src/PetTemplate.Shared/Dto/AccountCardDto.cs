namespace PetTemplate.Shared.Dto;

/// <summary>
/// Аккаунт целиком, без содержимого (спец. accounts, 7.3, 8.5 и 9): логин, способы войти,
/// пространства и кто в них ещё есть. Одна карточка на экран слияния, экран удаления
/// и карточку в админке — по ней человек решает, что удалить, а админ проверяет
/// владельца, не видя содержимого
/// </summary>
public class AccountCardDto
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public DateTime RegisteredAtUtc { get; set; }

    public bool HasPassword { get; set; }

    public bool HasTelegram { get; set; }

    public bool HasVk { get; set; }

    /// <summary>
    /// Аккаунт можно удалить: у него нет роли в админке (раздел 10). Нельзя — при слиянии
    /// он может только остаться (7.3), а удалить его можно, когда другой админ снимет роли
    /// </summary>
    public bool CanBeRemoved { get; set; } = true;

    /// <summary>
    /// Сколько сущностей проекта завёл сам человек (7.3). В ядре шаблона считать нечего —
    /// точка расширения: проект дописывает счётчик своей сущности в <see cref="AccountActivityDto"/>
    /// и в <c>AccountCards</c>
    /// </summary>
    public AccountActivityDto Activity { get; set; } = new();

    public List<AccountWorkspaceDto> Workspaces { get; set; } = [];
}

/// <summary>
/// Пространство в карточке аккаунта: что с ним будет, если аккаунт удалится, — видно
/// по <see cref="IsSolo"/>: он единственный участник — удалится со всем содержимым,
/// иначе останется остальным (раздел 10)
/// </summary>
public class AccountWorkspaceDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Владелец — с правом управления участниками; иначе — участник (7.3)</summary>
    public bool IsOwner { get; set; }

    /// <summary>Логины остальных участников — по ним админ сверяет владельца (8.5)</summary>
    public List<string> OtherMembers { get; set; } = [];

    /// <summary>Аккаунт — единственный участник: при удалении пространство удаляется (раздел 10)</summary>
    public bool IsSolo => OtherMembers.Count == 0;
}

/// <summary>
/// Активность аккаунта (7.3) — пустая в ядре шаблона. Раздел проекта, заводящий
/// сущность с автором, добавляет сюда её счётчик (например, <c>Issues</c>), а подсчёт —
/// в <c>AccountCards.Activity</c>; экран слияния и удаления показывает ненулевые
/// </summary>
public class AccountActivityDto
{
}
