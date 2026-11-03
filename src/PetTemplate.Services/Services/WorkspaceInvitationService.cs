using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Services.Services;

/// <summary>
/// Приглашения в пространство по ссылке (спец. authorization, 6).
/// Весь жизненный цикл приглашения живёт в <see cref="WorkspaceInvitation"/>;
/// членство принявшего — обычные строки <see cref="UserInPermission"/>,
/// как у всех остальных участников
/// </summary>
public class WorkspaceInvitationService(ApplicationContext db, AuthService auth, SessionGuard sessions)
{
    /// <summary>
    /// Выдаёт ссылку. Пустой набор прав отбила модель (<see cref="InvitationCreateDto"/>,
    /// <c>422</c>). Набор обязан быть вложен в права пригласившего: иначе
    /// <c>WorkspaceMemberCrud</c> превращается в способ выписать себе
    /// <c>PermissionManagement</c> через самоприглашение
    /// </summary>
    public async Task<WorkspaceInvitationDto> Create(InvitationCreateDto dto, Guid creatorId, CancellationToken ct = default)
    {
        var workspaceExists = await db.Workspaces.AnyAsync(x => x.Id == dto.WorkspaceId, ct);
        if (!workspaceExists)
        {
            throw new BadRequestException(InvitationMessages.WorkspaceNotFound);
        }

        var permissions = dto.Permissions
            .Where(x => x != PermissionEnum.None)
            .Distinct()
            .ToList();

        var creatorPermissions = await db.UserInPermissions
            .Where(x => x.UserId == creatorId && x.WorkspaceId == dto.WorkspaceId)
            .Select(x => x.PermissionId)
            .ToListAsync(ct);

        if (permissions.Except(creatorPermissions).Any())
        {
            throw new BadRequestException(WorkspaceMessages.PermissionsExceedOwn);
        }

        var now = DateTime.UtcNow;
        var inviteeName = dto.InviteeName?.Trim();

        var invitation = new WorkspaceInvitation
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = dto.WorkspaceId,
            CreatedBy = creatorId,
            // Именно v4: у v7 метка времени и всего 74 случайных бита,
            // а токен — это пропуск внутрь, и подбирать его никто не должен
            Token = Guid.NewGuid(),
            InviteeName = string.IsNullOrWhiteSpace(inviteeName) ? null : inviteeName,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(InvitationConstants.Lifetime)
        };

        foreach (var permission in permissions)
        {
            invitation.Permissions.Add(new WorkspaceInvitationPermission
            {
                InvitationId = invitation.Id,
                PermissionId = permission
            });
        }

        await db.WorkspaceInvitations.AddAsync(invitation, ct);
        await db.SaveChangesAsync(ct);

        return ToDto(invitation, acceptedByUserName: null, now);
    }

    /// <summary>
    /// Выданные приглашения пространства — вместе с токеном, чтобы страница участников
    /// умела показать и скопировать ссылку заново
    /// </summary>
    public async Task<List<WorkspaceInvitationDto>> GetList(Guid workspaceId, CancellationToken ct = default)
    {
        var invitations = await db.WorkspaceInvitations
            .AsNoTracking()
            .Include(x => x.Permissions)
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var acceptedByIds = invitations
            .Where(x => x.AcceptedBy.HasValue)
            .Select(x => x.AcceptedBy!.Value)
            .Distinct()
            .ToList();

        var userNames = await db.Profiles
            .Where(x => acceptedByIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UserName })
            .ToDictionaryAsync(x => x.Id, x => x.UserName, ct);

        var now = DateTime.UtcNow;

        return invitations
            .Select(x => ToDto(
                x,
                x.AcceptedBy.HasValue && userNames.TryGetValue(x.AcceptedBy.Value, out var name) ? name : null,
                now))
            .ToList();
    }

    /// <summary>
    /// Отзыв приглашения (спец. authorization, 6.2). Уже принятое не отзываем:
    /// участника из пространства это не выкинет, и кнопка обещала бы не то, что делает
    /// </summary>
    public async Task Revoke(Guid id, Guid workspaceId, CancellationToken ct = default)
    {
        var invitation = await db.WorkspaceInvitations
            .SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct);

        if (invitation == null)
        {
            throw new NotFoundException(InvitationMessages.NotFound);
        }

        if (invitation.AcceptedAtUtc != null)
        {
            throw new BadRequestException(InvitationMessages.CannotRevokeAccepted);
        }

        // Повторный отзыв не переписывает дату: отозвано — значит отозвано
        if (invitation.RevokedAtUtc != null) return;

        invitation.RevokedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Фоновая чистка (спец. authorization, 6.2): убирает приглашения, выданные больше
    /// <see cref="InvitationConstants.Retention"/> назад — все подряд, независимо
    /// от статуса. К этому сроку живых среди них нет (ссылка действует трое суток),
    /// а держать в списке выданных то, что отработало три недели назад, незачем.
    ///
    /// Считаем от <c>CreatedAtUtc</c>, а не от <c>ExpiresAtUtc</c> или даты расхода:
    /// «21 день с момента создания» — одно поле и один смысл на все статусы.
    ///
    /// Идемпотентно и безопасно при нескольких экземплярах API: повторный проход
    /// просто ничего не находит. Возвращает число удалённых строк
    /// </summary>
    public async Task<int> CleanupOld(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow - InvitationConstants.Retention;

        // Дочерние права уносит каскад внешнего ключа: <c>ExecuteDelete</c> идёт
        // в базу мимо <c>ChangeTracker</c>, и подтянуть их EF сам не может
        return await db.WorkspaceInvitations
            .Where(x => x.CreatedAtUtc < threshold)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Приглашение живое — не отозвано, не принято и не истекло. Нужно mini-app VK
    /// (спец. accounts, 5.6): приглашение приходит из неподписанного фрагмента запуска,
    /// и развилку пропускает только живое
    /// </summary>
    public async Task<bool> IsAlive(Guid token, CancellationToken ct = default)
    {
        var invitation = await db.WorkspaceInvitations.AsNoTracking().SingleOrDefaultAsync(x => x.Token == token, ct);
        return invitation != null && GetStatus(invitation, DateTime.UtcNow) == InvitationStatusEnum.Active;
    }

    /// <summary>
    /// Что показать на анонимном лендинге. Название пространства и ники отдаём только
    /// у живого приглашения: по мёртвой ссылке рассказывать о пространстве нечего
    /// </summary>
    public async Task<InvitationPreviewDto> Preview(Guid token, CancellationToken ct = default)
    {
        var invitation = await db.WorkspaceInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Token == token, ct);

        // Ненайденный токен — тот же ответ, что и просроченный: иначе эндпоинт
        // подтверждает существование токенов
        if (invitation == null)
        {
            return new InvitationPreviewDto { Status = InvitationStatusEnum.Expired };
        }

        var status = GetStatus(invitation, DateTime.UtcNow);

        if (status != InvitationStatusEnum.Active)
        {
            return new InvitationPreviewDto { Status = status };
        }

        // Здесь именно SingleAsync, а не SingleOrDefaultAsync с 404: пространство взято
        // у живого приглашения и держится внешним ключом. Промах тут —
        // не «чужой id в запросе», а настоящая поломка, и ответ ей 500
        var workspaceName = await db.Workspaces
            .Where(x => x.Id == invitation.WorkspaceId)
            .Select(x => x.Name)
            .SingleAsync(ct);

        // Пригласившего может уже не быть (удалён аккаунт — ссылка пустеет):
        // приглашение от этого не умирает, просто без подписи
        var inviterName = await db.Profiles
            .Where(x => x.Id == invitation.CreatedBy)
            .Select(x => x.UserName)
            .SingleOrDefaultAsync(ct);

        return new InvitationPreviewDto
        {
            Status = status,
            WorkspaceName = workspaceName,
            InviterName = inviterName,
            InviteeName = invitation.InviteeName
        };
    }

    /// <summary>
    /// Принятие приглашения уже вошедшим пользователем (спец. authorization, 6.3,
    /// сценарий А). Всё уходит одним <c>SaveChanges</c>: права, расход ссылки и — если
    /// пространство у пользователя стало единственным — указатель на основное
    /// </summary>
    public async Task<UserDto> Accept(Guid token, Guid userId, CancellationToken ct = default)
    {
        var invitation = await LoadActive(token, ct);

        var isMember = await db.UserInPermissions
            .AnyAsync(x => x.UserId == userId && x.WorkspaceId == invitation.WorkspaceId, ct);

        // Ссылку при этом не расходуем: участник по ней уже ничего не получит,
        // а приглашали, возможно, не его
        if (isMember)
        {
            throw new BadRequestException(InvitationMessages.AlreadyMember);
        }

        var profile = await db.Profiles.SingleAsync(x => x.Id == userId, ct);
        var hadWorkspaces = await db.UserInPermissions.AnyAsync(x => x.UserId == userId, ct);

        Consume(invitation, profile, isFirstWorkspace: !hadWorkspaces);

        await SaveConsumed(ct);
        // Права в новом пространстве — со следующего запроса, а не с истечением кэша
        sessions.Forget(userId);

        return await auth.GetUser(userId, ct);
    }

    /// <summary>
    /// Регистрация по ссылке (спец. authorization, 6.3, сценарий Б). Профиль, права
    /// и расход ссылки — одним <c>SaveChanges</c>: иначе падение на середине оставило бы
    /// человека зарегистрированным, но не принятым, а ссылку — израсходованной
    /// </summary>
    public async Task<UserDto> RegisterAndAccept(InvitationRegisterDto dto, CancellationToken ct = default)
    {
        var invitation = await LoadActive(dto.Token, ct);

        // Почту и телефон форма не спрашивает, поэтому занятым может быть только логин —
        // отказ тот же, что у обычной регистрации (`AuthMessages.UserNameTaken`)
        var profile = await auth.CreateProfile(dto.UserName, dto.Password, email: null, phone: null, dto.Language, ct);

        // Пользователь только что заведён — пространство у него заведомо первое
        Consume(invitation, profile, isFirstWorkspace: true);

        await SaveConsumed(ct);

        return await auth.GetUser(profile.Id, ct);
    }

    /// <summary>
    /// Расходует приглашение: права принявшему, отметка о расходе и, если пространство
    /// у него единственное, — указатель на основное. Ничего не сохраняет — всё уходит
    /// одним <c>SaveChanges</c> вызывающего, вместе с самим членством.
    ///
    /// Точка расширения: если разделу проекта нужно что-то завести новичку (настройки,
    /// заготовки), это делается здесь, тем же сохранением
    /// </summary>
    private void Consume(WorkspaceInvitation invitation, Profile profile, bool isFirstWorkspace)
    {
        var permissions = invitation.Permissions
            .Select(x => x.PermissionId)
            .Distinct()
            .Select(permission => new UserInPermission
            {
                WorkspaceId = invitation.WorkspaceId,
                UserId = profile.Id,
                PermissionId = permission
            });

        db.UserInPermissions.AddRange(permissions);

        invitation.AcceptedAtUtc = DateTime.UtcNow;
        invitation.AcceptedBy = profile.Id;

        if (isFirstWorkspace)
        {
            profile.PrimaryWorkspaceId = invitation.WorkspaceId;
        }
    }

    /// <summary>
    /// Сохраняет расход ссылки. <c>AcceptedBy</c> — токен конкуренции, поэтому
    /// расход уходит условным <c>UPDATE … WHERE accepted_by IS NULL</c>: у второго
    /// из двух одновременных запросов он не меняет ни одной строки, и EF отвечает
    /// <see cref="DbUpdateConcurrencyException"/>. Для пришедшего это тот же отказ,
    /// что и по уже израсходованной ссылке, — так он его и видит.
    ///
    /// Сохранение одно на весь запрос, поэтому откат полный: регистрация по ссылке
    /// не оставит человека заведённым, но не принятым
    /// </summary>
    private async Task SaveConsumed(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BadRequestException(InvitationMessages.Accepted);
        }
        catch (DbUpdateException ex) when (DbErrors.IsDuplicate(ex, DbErrors.UserNameIndex))
        {
            // Логин заняли между проверкой и записью — тот же отказ, а не 500
            throw new BadRequestException(InvitationMessages.UserNameTaken);
        }
    }

    /// <summary>
    /// Приглашение, по которому ещё можно пройти. Мёртвое отвечает тем же текстом,
    /// что лендинг рисует по статусу из <see cref="Preview"/>
    /// </summary>
    private async Task<WorkspaceInvitation> LoadActive(Guid token, CancellationToken ct)
    {
        var invitation = await db.WorkspaceInvitations
            .Include(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.Token == token, ct);

        // Ненайденный токен — тот же ответ, что и просроченный
        if (invitation == null)
        {
            throw new BadRequestException(InvitationMessages.Expired);
        }

        var status = GetStatus(invitation, DateTime.UtcNow);

        return status switch
        {
            InvitationStatusEnum.Accepted => throw new BadRequestException(InvitationMessages.Accepted),
            InvitationStatusEnum.Revoked => throw new BadRequestException(InvitationMessages.Revoked),
            InvitationStatusEnum.Expired => throw new BadRequestException(InvitationMessages.Expired),
            _ => invitation
        };
    }

    /// <summary>
    /// Статус считаем по трём полям строки, отдельной колонки не держим:
    /// просрочка — это сравнение с текущим временем, а не запись в базе
    /// </summary>
    private static InvitationStatusEnum GetStatus(WorkspaceInvitation invitation, DateTime now)
    {
        if (invitation.AcceptedAtUtc != null) return InvitationStatusEnum.Accepted;
        if (invitation.RevokedAtUtc != null) return InvitationStatusEnum.Revoked;
        if (invitation.ExpiresAtUtc <= now) return InvitationStatusEnum.Expired;

        return InvitationStatusEnum.Active;
    }

    private static WorkspaceInvitationDto ToDto(WorkspaceInvitation invitation, string? acceptedByUserName, DateTime now)
    {
        return new WorkspaceInvitationDto
        {
            Id = invitation.Id,
            Token = invitation.Token,
            InviteeName = invitation.InviteeName,
            Status = GetStatus(invitation, now),
            CreatedAtUtc = invitation.CreatedAtUtc,
            ExpiresAtUtc = invitation.ExpiresAtUtc,
            AcceptedByUserName = acceptedByUserName,
            Permissions = invitation.Permissions.Select(x => x.PermissionId).ToList()
        };
    }
}
