using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Выданное приглашение — и в ответе на создание, и в списке выданных.
/// <c>Token</c> отдаём наружу осознанно: страница участников должна уметь показать
/// и скопировать ссылку заново, иначе выданное приглашение нельзя переслать второй раз
/// </summary>
public class WorkspaceInvitationDto
{
    public Guid Id { get; set; }

    /// <summary>
    /// Хвост ссылки: базовый адрес собирает клиент из <c>window.location.origin</c>
    /// </summary>
    public Guid Token { get; set; }

    public string? InviteeName { get; set; }

    /// <summary>
    /// Считается в памяти по трём полям строки — отдельной колонки-статуса нет
    /// </summary>
    public InvitationStatusEnum Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Ник того, кто пришёл по ссылке; пусто, пока не пришли
    /// </summary>
    public string? AcceptedByUserName { get; set; }

    public List<PermissionEnum> Permissions { get; set; } = [];
}
