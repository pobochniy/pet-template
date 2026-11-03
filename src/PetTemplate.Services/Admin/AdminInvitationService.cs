using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Приглашения админов (спец. admin, 3.4) — намеренное повторение приглашений
/// в пространство (<see cref="WorkspaceInvitationService"/>): одноразовая ссылка, токен v4, срок
/// <see cref="AdminOptions.AdminInviteTtl"/>, отзыв непринятого, чистка через
/// <see cref="InvitationConstants.Retention"/>.
///
/// Принимается входом под обычной учёткой продукта: пароля у админки своего нет,
/// и ролей у принимающего ещё нет — поэтому <see cref="Accept"/> и
/// <see cref="Preview"/> живут вне <c>[AuthorizeAdmin]</c>.
///
/// Отказы выдающему — текстом, без кодов (3.5)
/// </summary>
public class AdminInvitationService(
    ApplicationContext db,
    AdminAccessService access,
    AdminMemberService members,
    IOptions<AdminOptions> options)
{
    public async Task<AdminInvitationDto[]> List(CancellationToken ct = default)
    {
        var invitations = await db.AdminInvitations
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new
            {
                Invitation = x,
                // Выдавший, принявший и отозвавший могли уйти из системы — строка остаётся без имени
                CreatedByName = db.Profiles
                    .Where(p => p.Id == x.CreatedByUserId)
                    .Select(p => p.UserName)
                    .FirstOrDefault(),
                AcceptedByName = db.Profiles
                    .Where(p => p.Id == x.AcceptedByUserId)
                    .Select(p => p.UserName)
                    .FirstOrDefault(),
                RevokedByName = db.Profiles
                    .Where(p => p.Id == x.RevokedByUserId)
                    .Select(p => p.UserName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        return invitations
            .Select(x =>
            {
                var dto = ToDto<AdminInvitationDto>(x.Invitation, x.CreatedByName, x.AcceptedByName, now);
                dto.RevokedByName = x.RevokedByName;
                return dto;
            })
            .ToArray();
    }

    /// <summary>
    /// Выдаёт ссылку. Набор ролей вложен в роли выдающего — из базы, а не из токена:
    /// иначе приглашение стало бы способом выписать себе чужую роль через второй аккаунт.
    /// Токен уходит только в этом ответе, в базе — его хеш: из списка чужую ссылку не
    /// взять, а то принял бы её сам и получил роль, которой у него нет
    /// </summary>
    public async Task<AdminInvitationIssuedDto> Create(AdminInvitationCreateDto dto, Guid actorId, CancellationToken ct = default)
    {
        var roles = dto.Roles.Distinct().ToArray();

        if (roles.Length == 0) throw new AdminRejectionException("Отметьте хотя бы одну роль: без ролей человек не админ");
        if (!roles.All(AdminRoleSet.IsSingle)) throw new AdminRejectionException("Неизвестная роль");

        var own = await members.EnsureManager(actorId, ct);
        if (roles.Except(own).Any()) throw new AdminRejectionException("Нельзя выдать роль, которой нет у вас самих");

        var now = DateTime.UtcNow;

        // Именно v4: у v7 метка времени и всего 74 случайных бита,
        // а токен — это пропуск внутрь
        var token = Guid.NewGuid();

        var invitation = new AdminInvitation
        {
            Id = Guid.CreateVersion7(),
            TokenHash = AdminInvitation.HashOf(token),
            Roles = AdminRoleSet.Join(roles),
            Status = AdminInvitationStatus.Pending,
            CreatedUtc = now,
            ExpiresUtc = now.Add(options.Value.AdminInviteTtl),
            CreatedByUserId = actorId
        };

        db.AdminInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        var createdByName = await db.Profiles
            .Where(p => p.Id == actorId)
            .Select(p => p.UserName)
            .FirstOrDefaultAsync(ct);

        var issued = ToDto<AdminInvitationIssuedDto>(invitation, createdByName, acceptedByName: null, now);
        issued.Token = token;
        return issued;
    }

    /// <summary>
    /// Отзыв непринятого — и просроченного тоже, от этого вреда нет. Принятое не
    /// отзывается: роли у принявшего снимаются снятием ролей, а кнопка отзыва обещала
    /// бы не то, что делает. <c>false</c> — приглашения нет
    /// </summary>
    public async Task<bool> Revoke(Guid id, Guid actorId, CancellationToken ct = default)
    {
        await members.EnsureManager(actorId, ct);

        var invitation = await db.AdminInvitations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (invitation == null) return false;

        if (invitation.Status == AdminInvitationStatus.Accepted)
            throw new AdminRejectionException("Приглашение уже принято — снимите роли у админа");

        // Повторный отзыв ничего не меняет
        if (invitation.Status == AdminInvitationStatus.Revoked) return true;

        invitation.Status = AdminInvitationStatus.Revoked;
        invitation.RevokedByUserId = actorId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Что показать на странице приглашения до входа. <c>null</c> — токена нет вовсе;
    /// у отработавшего — только статус, без имени пригласившего и ролей
    /// </summary>
    public async Task<AdminInvitationPreviewDto?> Preview(Guid token, CancellationToken ct = default)
    {
        var hash = AdminInvitation.HashOf(token);
        var invitation = await db.AdminInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);

        if (invitation == null) return null;

        var status = StatusOf(invitation, DateTime.UtcNow);
        if (status != AdminInvitationStatus.Pending) return new AdminInvitationPreviewDto { Status = status };

        return new AdminInvitationPreviewDto
        {
            Status = status,
            // Только логин: страницу открывает всякий, у кого есть ссылка, а почта и
            // телефон выдавшего — не то, что ему положено видеть
            InviterName = await db.Profiles
                .Where(p => p.Id == invitation.CreatedByUserId)
                .Select(p => p.UserName)
                .FirstOrDefaultAsync(ct),
            Roles = AdminRoleSet.Split(invitation.Roles)
        };
    }

    /// <summary>
    /// Принятие: логин и пароль от продукта плюс живой токен. Роли из приглашения
    /// и отметка <c>Accepted</c> уходят одним <c>SaveChanges</c> — он сам себе
    /// транзакция. Отвечает админом со всеми его ролями — под них контроллер сразу
    /// выпускает токен, второй вход не нужен.
    ///
    /// Уже админу приглашение дописывает недостающие роли: выдавать больше, чем в
    /// ссылке, оно не может, а отказать было бы странно.
    ///
    /// Роли ссылки пересекаются с теми, что у выдавшего сейчас, — из базы: удалённый
    /// админ или тот, с кого сняли <c>AdminManagement</c>, своими ссылками больше
    /// ничего не раздаёт.
    ///
    /// <c>null</c> — на любую неудачу: неверный пароль, мёртвый или чужой токен,
    /// выдавший больше не управляет админами, второй из двух одновременных принимающих
    /// (той же ссылки или другой, с общей ролью). Наружу всё это — один <c>404</c>,
    /// как у <c>Admin/Login</c>: это вторая дверь, и запирается она так же
    /// </summary>
    public async Task<AdminDto?> Accept(Guid token, LoginDto dto, CancellationToken ct = default)
    {
        var user = await access.Authenticate(dto, ct);
        if (user == null) return null;

        var hash = AdminInvitation.HashOf(token);
        var invitation = await db.AdminInvitations.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (invitation == null || StatusOf(invitation, DateTime.UtcNow) != AdminInvitationStatus.Pending) return null;

        // Ссылка выдаёт не больше, чем у выдавшего есть сейчас: снятые с него роли
        // и сам он, уже не управляющий админами, через неё не раздаются
        // Выдавшего уже нет (аккаунт удалён) — ссылка ничего не выдаёт
        if (invitation.CreatedByUserId is not { } inviterId) return null;
        var inviter = await members.RolesOf(inviterId, ct);
        if (!inviter.Contains(AdminRole.AdminManagement)) return null;

        var roles = AdminRoleSet.Split(invitation.Roles).Intersect(inviter).ToArray();
        if (roles.Length == 0) return null;

        var now = DateTime.UtcNow;
        var current = await members.RolesOf(user.Id, ct);

        db.UserInAdminRoles.AddRange(roles
            .Except(current)
            .Select(role => new UserInAdminRole
            {
                UserId = user.Id,
                Role = role,
                GrantedUtc = now,
                GrantedByUserId = invitation.CreatedByUserId
            }));

        invitation.Status = AdminInvitationStatus.Accepted;
        invitation.AcceptedByUserId = user.Id;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Status — токен конкуренции: по ссылке только что прошёл кто-то ещё
            return null;
        }
        catch (DbUpdateException e) when (DbErrors.IsDuplicate(e, DbErrors.AdminRoleKey))
        {
            // Две ссылки с общей ролью, принятые одним человеком одновременно: вторая
            // упёрлась в ключ (UserId, Role), который только что записала первая.
            // Ничего не сохранилось, ссылка жива — её можно принять ещё раз
            return null;
        }

        return await access.Admin(user, dto.Login, ct);
    }

    /// <summary>
    /// Суточная чистка (3.4): через <see cref="InvitationConstants.Retention"/> после
    /// выдачи — в любом статусе, по тому же правилу, что приглашения в пространство. Живых к этому
    /// сроку нет. Возвращает число удалённых строк
    /// </summary>
    public async Task<int> CleanupOld(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow - InvitationConstants.Retention;

        return await db.AdminInvitations
            .Where(x => x.CreatedUtc < threshold)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Просрочка — сравнение со временем, в базу она не пишется</summary>
    private static AdminInvitationStatus StatusOf(AdminInvitation invitation, DateTime now) =>
        invitation.Status == AdminInvitationStatus.Pending && invitation.ExpiresUtc <= now
            ? AdminInvitationStatus.Expired
            : invitation.Status;

    private static T ToDto<T>(
        AdminInvitation invitation,
        string? createdByName,
        string? acceptedByName,
        DateTime now) where T : AdminInvitationDto, new() =>
        new()
        {
            Id = invitation.Id,
            Roles = AdminRoleSet.Split(invitation.Roles),
            Status = StatusOf(invitation, now),
            CreatedUtc = invitation.CreatedUtc,
            ExpiresUtc = invitation.ExpiresUtc,
            CreatedByName = createdByName,
            AcceptedByName = acceptedByName
        };
}
