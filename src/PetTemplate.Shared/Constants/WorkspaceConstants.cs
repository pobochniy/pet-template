namespace PetTemplate.Shared.Constants;

/// <summary>
/// Ограничения пространства (спец. authorization, 1.2, 4 и 8).
/// Общие для сущности, DTO, сервиса, фоновой чистки и тестов.
/// </summary>
public static class WorkspaceConstants
{
    /// <summary>
    /// Заголовок активного пространства. Ставит его интерцептор клиента
    /// (<c>workspace.interceptor.ts</c>), читает <c>HttpContextExtensions.GetWorkspaceId</c>
    /// </summary>
    public const string Header = "X-Workspace-Id";

    /// <summary>
    /// Максимальная длина названия пространства
    /// </summary>
    public const int NameMaxLength = 100;

    /// <summary>
    /// Сколько заброшенное пространство — без единого участника — ждёт удаления
    /// фоновой чисткой с момента последнего действия внутри него
    /// </summary>
    public static readonly TimeSpan AbandonedRetention = TimeSpan.FromDays(180);
}
