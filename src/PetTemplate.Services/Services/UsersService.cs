using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Services.Services;

/// <summary>
/// Состав пространства и права участников (спец. authorization, 5)
/// </summary>
public class UsersService(ApplicationContext context)
{
    /// <summary>
    /// Участники пространства: те, у кого есть права в нём. Списки участников в разделах
    /// работают в контексте одного пространства, поэтому люди из чужих сюда
    /// попадать не должны.
    ///
    /// Без контактов (<see cref="ProfileSummaryDto"/>): почта и телефон —
    /// только в своём профиле. Участники друг другу доверяют (спец. authorization, 1), но
    /// контакт — не то, что нужно экрану со списком, а утечь из списка ему проще всего
    /// </summary>
    public async Task<ProfileSummaryDto[]> GetProfiles(Guid workspaceId, CancellationToken ct = default)
    {
        // Идентификаторы отбираем отдельно: у пользователя в пространстве несколько прав,
        // и join по ним возвращал бы один и тот же профиль столько раз, сколько у него прав
        var memberIds = context.UserInPermissions
            .Where(up => up.WorkspaceId == workspaceId)
            .Select(up => up.UserId)
            .Distinct();

        return await context.Profiles
            .Where(p => memberIds.Contains(p.Id))
            .OrderBy(p => p.UserName)
            .Select(p => new ProfileSummaryDto
            {
                Id = p.Id,
                UserName = p.UserName,
                AvatarId = p.AvatarId
            })
            .ToArrayAsync(ct);
    }

    public async Task<PermissionEnum[]> GetPermissions(Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        return await context.UserInPermissions
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .Select(x => x.PermissionId)
            .ToArrayAsync(ct);
    }

    /// <summary>
    /// Полностью заменяет права пользователя в пространстве.
    ///
    /// Пустой набор сюда не доезжает — его отбивает модель (<see cref="UserAndPermissionsDto"/>,
    /// <c>422</c> у поля): членство в пространстве — это и есть строки
    /// <c>UserInPermissions</c>, и снятие последней галки молча выкидывало бы
    /// человека из пространства. Убирают участника отдельным явным действием
    /// (<see cref="WorkspaceService.RemoveMember"/>).
    ///
    /// Остальные отказы смотрят в базу, поэтому они <c>400</c> (спец. authorization, 5):
    /// <list type="bullet">
    /// <item>распорядителя прав не снимаем последнего: без него править доступы
    /// в пространстве станет некому, а чаще всего это право снимают с самого себя;</item>
    /// <item>цель обязана быть участником: правка прав не заводит участников;</item>
    /// <item>новое право обязано быть у <paramref name="actorId"/> — симметрично выдаче
    /// приглашения. Иначе <c>PermissionManagement</c> превращается в способ выписать себе
    /// что угодно, а ограничение в приглашениях обходится в два клика.</item>
    /// </list>
    ///
    /// Транзакция здесь явная, и это не лишнее: <c>ExecuteDelete</c> уходит в базу
    /// сразу, отдельным запросом, а не в общий <c>SaveChanges</c> — без транзакции
    /// сбой на вставке оставил бы пользователя вообще без прав
    /// </summary>
    public async Task SetPermissions(Guid userId, Guid workspaceId, IEnumerable<PermissionEnum> permissions,
        Guid actorId, CancellationToken ct = default)
    {
        // Дедупликация прав; None — не право, а «ничего не выбрано»
        var uniquePermissions = permissions
            .Where(x => x != PermissionEnum.None)
            .Distinct()
            .ToList();

        var currentPermissions = await PermissionsOf(userId, workspaceId, ct);

        // Заводить участника мимо приглашения этот метод не должен: пустой набор в базе —
        // это и есть «не участник», а неизвестный userId иначе упал бы 500 на внешнем ключе
        if (currentPermissions.Count == 0)
        {
            throw new BadRequestException(WorkspaceMessages.MemberNotFound);
        }

        var actorPermissions = actorId == userId
            ? currentPermissions
            : await PermissionsOf(actorId, workspaceId, ct);

        // Сверяем только то, что добавляют: чужой набор, собранный кем-то более
        // правым, можно сохранить как есть — иначе править права участника,
        // у которого их больше, стало бы нельзя вовсе
        var granted = uniquePermissions.Except(currentPermissions);

        if (granted.Except(actorPermissions).Any())
        {
            throw new BadRequestException(WorkspaceMessages.PermissionsExceedOwn);
        }

        if (!uniquePermissions.Contains(PermissionEnum.PermissionManagement))
        {
            await EnsureNotLastPermissionManager(userId, workspaceId, ct);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        await context.UserInPermissions
            .Where(up => up.UserId == userId && up.WorkspaceId == workspaceId)
            .ExecuteDeleteAsync(ct);

        // Удалённые мимо трекера строки в нём ещё живы — без очистки вставка тех же
        // ключей упала бы на «уже отслеживается»
        context.ChangeTracker.Clear();

        var newPermissions = uniquePermissions.Select(permission => new UserInPermission
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            PermissionId = permission
        });

        await context.UserInPermissions.AddRangeAsync(newPermissions, ct);
        await context.SaveChangesAsync(ct);

        // Откат писать не нужно: незакоммиченную транзакцию отменит `await using`
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Права одного участника в пространстве. Пустой список — не участник:
    /// членство и есть эти строки (спец. authorization, 1)
    /// </summary>
    private async Task<List<PermissionEnum>> PermissionsOf(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        return await context.UserInPermissions
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .Select(x => x.PermissionId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Отбиваем снятие <see cref="PermissionEnum.PermissionManagement"/> с последнего,
    /// кто им владеет: пространство без распорядителя прав починить будет некому — правка
    /// доступов закрыта тем же правом, которое только что исчезло.
    ///
    /// Спрашиваем именно владельцев права, а не «есть ли кто-то ещё»: снимать нечего,
    /// когда права у участника и не было, и такой запрос отбивать незачем
    /// </summary>
    private async Task EnsureNotLastPermissionManager(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var managers = await context.UserInPermissions
            .Where(x => x.WorkspaceId == workspaceId
                        && x.PermissionId == PermissionEnum.PermissionManagement)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (managers.Count == 1 && managers[0] == userId)
        {
            throw new BadRequestException(WorkspaceMessages.LastPermissionManager);
        }
    }
}
