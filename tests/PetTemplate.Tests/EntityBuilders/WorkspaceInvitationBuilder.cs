using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Tests.EntityBuilders;

/// <summary>
/// Приглашение в пространство. По умолчанию живое: выдано сейчас, срок
/// <see cref="InvitationConstants.Lifetime"/>, не принято и не отозвано
/// </summary>
public class WorkspaceInvitationBuilder
{
    private readonly WorkspaceInvitation _invitation;

    public WorkspaceInvitationBuilder(Guid workspaceId, Guid createdBy)
    {
        var now = DateTime.UtcNow;

        _invitation = new WorkspaceInvitation
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            CreatedBy = createdBy,
            Token = Guid.NewGuid(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(InvitationConstants.Lifetime)
        };
    }

    public WorkspaceInvitationBuilder WithPermissions(params PermissionEnum[] permissions)
    {
        foreach (var permission in permissions)
        {
            _invitation.Permissions.Add(new WorkspaceInvitationPermission
            {
                InvitationId = _invitation.Id,
                PermissionId = permission
            });
        }

        return this;
    }

    public WorkspaceInvitationBuilder WithInviteeName(string name)
    {
        _invitation.InviteeName = name;
        return this;
    }

    /// <summary>
    /// Выдано в прошлом: срок считается от выдачи, как у настоящего
    /// </summary>
    public WorkspaceInvitationBuilder CreatedAt(DateTime utc)
    {
        _invitation.CreatedAtUtc = utc;
        _invitation.ExpiresAtUtc = utc.Add(InvitationConstants.Lifetime);
        return this;
    }

    public WorkspaceInvitationBuilder Accepted(Guid userId)
    {
        _invitation.AcceptedAtUtc = DateTime.UtcNow;
        _invitation.AcceptedBy = userId;
        return this;
    }

    public WorkspaceInvitationBuilder Revoked()
    {
        _invitation.RevokedAtUtc = DateTime.UtcNow;
        return this;
    }

    public WorkspaceInvitation Please()
    {
        return _invitation;
    }
}
