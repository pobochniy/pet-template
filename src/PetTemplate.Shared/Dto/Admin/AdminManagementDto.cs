using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto.Admin;

/// <summary>Строка вкладки «Админы» (спец. admin, 3.1): человек и его роли из базы</summary>
public class AdminMemberDto
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = null!;

    public AdminMemberRoleDto[] Roles { get; set; } = [];
}

/// <summary>Роль и след выдачи. <see cref="GrantedByName"/> пусто у первого админа и у ушедшего выдавшего</summary>
public class AdminMemberRoleDto
{
    public AdminRole Role { get; set; }

    public DateTime GrantedUtc { get; set; }

    public string? GrantedByName { get; set; }
}

/// <summary>
/// Выданное приглашение (спец. admin, 3.4) — без токена: в базе лежит только его
/// хеш, а ссылка показывается один раз, в <see cref="AdminInvitationIssuedDto"/>
/// </summary>
public class AdminInvitationDto
{
    public Guid Id { get; set; }

    /// <summary>По роли на элемент — флаги разворачивает сервер</summary>
    public AdminRole[] Roles { get; set; } = [];

    public AdminInvitationStatus Status { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }

    public string? CreatedByName { get; set; }

    public string? AcceptedByName { get; set; }

    public string? RevokedByName { get; set; }
}

/// <summary>Ответ на выдачу — единственное место, где токен уходит наружу</summary>
public class AdminInvitationIssuedDto : AdminInvitationDto
{
    public Guid Token { get; set; }
}

/// <summary><c>POST Admin/Invitations</c>: набор ролей — не больше своих</summary>
public class AdminInvitationCreateDto
{
    public AdminRole[] Roles { get; set; } = [];
}

/// <summary>
/// Что видит страница <c>/invite/{token}</c> до входа: статус, а у живого приглашения —
/// кто позвал и какие роли выдаются. У отработавшего имён нет: рассказывать нечего
/// </summary>
public class AdminInvitationPreviewDto
{
    public AdminInvitationStatus Status { get; set; }

    public string? InviterName { get; set; }

    public AdminRole[] Roles { get; set; } = [];
}
