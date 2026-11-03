using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Services;

/// <summary>
/// Карточка аккаунта (спец. accounts, 7.3, 8.5 и 9) — одна на экран слияния, экран
/// удаления и карточку в админке. Только названия, логины и числа: содержимого в ней
/// нет, админ проверяет по ней владельца, не видя его.
///
/// Точка расширения: раздел проекта, заводящий сущность с автором, дописывает её счётчик
/// в <see cref="Build"/> (и поле в <see cref="AccountActivityDto"/>), а число в
/// пространстве — в <see cref="AccountWorkspaceDto"/> и <see cref="Workspaces"/>: человек
/// должен видеть, что удалится вместе с пространством, где он один
/// </summary>
public class AccountCards(ApplicationContext db)
{
    public async Task<AccountCardDto> Build(Guid userId, CancellationToken ct = default)
    {
        var account = await db.Profiles
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.UserName,
                x.User!.CreatedAtUtc,
                HasPassword = x.User.PasswordHash != null,
                HasTelegram = x.User.TelegramId != null,
                HasVk = x.User.VkId != null
            })
            .SingleAsync(ct);

        return new AccountCardDto
        {
            UserId = userId,
            UserName = account.UserName,
            RegisteredAtUtc = account.CreatedAtUtc,
            HasPassword = account.HasPassword,
            HasTelegram = account.HasTelegram,
            HasVk = account.HasVk,
            // Аккаунт с ролью админки удалить нельзя (раздел 10): при слиянии он может только остаться
            CanBeRemoved = !await db.UserInAdminRoles.AnyAsync(x => x.UserId == userId, ct),
            // Сколько сущностей проекта завёл сам человек — в ядре считать нечего. Точка
            // расширения: подсчёт своей сущности, например `db.Issues.CountAsync(x => x.CreatedBy == userId)`
            Activity = new AccountActivityDto(),
            Workspaces = await Workspaces(userId, ct)
        };
    }

    /// <summary>
    /// Состав пространств аккаунта: id и число участников. Снимок этого набора держит
    /// ключ слияния — изменился между просмотром и подтверждением, значит человек
    /// подтверждал не то, что случится (7.4)
    /// </summary>
    public async Task<string[]> Composition(Guid userId, CancellationToken ct = default)
    {
        var workspaceIds = await WorkspaceIds(userId, ct);
        var members = await db.UserInPermissions
            .Where(x => workspaceIds.Contains(x.WorkspaceId))
            .Select(x => new { x.WorkspaceId, x.UserId })
            .Distinct()
            .ToListAsync(ct);

        return members
            .GroupBy(x => x.WorkspaceId)
            .Select(g => $"{g.Key}:{g.Count()}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private Task<List<Guid>> WorkspaceIds(Guid userId, CancellationToken ct) =>
        db.UserInPermissions
            .Where(x => x.UserId == userId)
            .Select(x => x.WorkspaceId)
            .Distinct()
            .ToListAsync(ct);

    private async Task<List<AccountWorkspaceDto>> Workspaces(Guid userId, CancellationToken ct)
    {
        var workspaceIds = await WorkspaceIds(userId, ct);
        if (workspaceIds.Count == 0) return [];

        var permissions = await db.UserInPermissions
            .Where(x => workspaceIds.Contains(x.WorkspaceId))
            .Select(x => new { x.WorkspaceId, x.UserId, x.PermissionId })
            .ToListAsync(ct);
        var memberIds = permissions.Select(x => x.UserId).Distinct().ToList();
        var names = await db.Profiles
            .Where(x => memberIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserName, ct);
        var workspaces = await db.Workspaces
            .Where(x => workspaceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return workspaceIds
            .Select(id => new AccountWorkspaceDto
            {
                Id = id,
                Name = workspaces.GetValueOrDefault(id, ""),
                IsOwner = permissions.Any(x => x.WorkspaceId == id && x.UserId == userId
                                               && x.PermissionId == PermissionEnum.PermissionManagement),
                OtherMembers = permissions
                    .Where(x => x.WorkspaceId == id && x.UserId != userId)
                    .Select(x => x.UserId)
                    .Distinct()
                    .Select(x => names.GetValueOrDefault(x, ""))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
