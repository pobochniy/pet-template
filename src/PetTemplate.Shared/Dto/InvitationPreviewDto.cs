using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Что видит на лендинге тот, кто перешёл по ссылке (спец. authorization, 6.3).
/// Ни <c>workspaceId</c>, ни идентификаторов пользователей: лендинг анонимный,
/// лишнего наружу не отдаём
/// </summary>
public class InvitationPreviewDto
{
    public InvitationStatusEnum Status { get; set; }

    /// <summary>
    /// Название пространства — только у живого приглашения; иначе пусто
    /// </summary>
    public string? WorkspaceName { get; set; }

    /// <summary>
    /// Ник пригласившего — «Вася приглашает вас в пространство „…“»
    /// </summary>
    public string? InviterName { get; set; }

    /// <summary>
    /// Имя приглашаемого — «Привет, Валера!»
    /// </summary>
    public string? InviteeName { get; set; }
}
