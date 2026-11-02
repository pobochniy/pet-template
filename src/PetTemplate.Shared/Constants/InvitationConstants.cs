namespace PetTemplate.Shared.Constants;

/// <summary>
/// Ограничения приглашений в пространство (спец. authorization, 6).
/// Общие для сущности, DTO, сервиса и фоновой чистки.
/// </summary>
public static class InvitationConstants
{
    /// <summary>
    /// Сколько живёт выданная ссылка
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(3);

    /// <summary>
    /// Через сколько после выдачи строка приглашения уходит фоновой чисткой —
    /// вместе с уже отработавшими и просроченными
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(21);

    /// <summary>
    /// Максимальная длина имени приглашаемого
    /// </summary>
    public const int InviteeNameMaxLength = 50;
}
