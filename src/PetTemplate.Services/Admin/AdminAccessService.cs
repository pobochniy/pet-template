using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Кто такой админ (спец. admin, 3.1 и 3.3): пользователь продукта с хотя бы
/// одной строкой <see cref="UserInAdminRole"/>. Своей учётки и своего пароля у админки
/// нет — проверка входа та же, что в продукте, плюс наличие ролей.
/// </summary>
public class AdminAccessService(ApplicationContext db, AuthService auth)
{
    /// <summary>
    /// Вход в админку. <c>null</c> — и на неверный пароль, и на пользователя без ролей:
    /// наружу оба уезжают одним и тем же <c>404</c>, и отличать их незачем уже здесь
    /// </summary>
    public async Task<AdminDto?> LogIn(LoginDto dto, CancellationToken ct = default)
    {
        var user = await Authenticate(dto, ct);

        return user == null ? null : await Admin(user, dto.Login, ct);
    }

    /// <summary>
    /// Чей логин — до проверки пароля, для лимита попыток (3.3): ник, почта и телефон
    /// одной учётки считаются одним счётчиком. <c>null</c> — такого логина нет
    /// </summary>
    public async Task<Guid?> OwnerOf(LoginDto dto, CancellationToken ct = default) =>
        await auth.UserIdOf(dto, ct);

    /// <summary>
    /// Логин и пароль от продукта — без ролей. <c>null</c> на неверную пару:
    /// <see cref="AuthService.LogIn"/> бросает на неё <see cref="UnauthorizedAccessException"/>,
    /// а вход в админку отвечает на это голым <c>404</c>. Им же проверяет пароль
    /// принятие приглашения (3.4) — ролей у принимающего ещё нет
    /// </summary>
    internal async Task<UserDto?> Authenticate(LoginDto dto, CancellationToken ct)
    {
        try
        {
            return await auth.LogIn(dto, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Роли из базы, а не из токена, по возрастанию номера. Ими проверка админского токена
    /// урезает его роли на каждом запросе (3.3): снятая роль не должна дожить в токене
    /// до конца его часа
    /// </summary>
    public async Task<AdminRole[]> RolesOf(Guid userId, CancellationToken ct = default) =>
        await db.UserInAdminRoles
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Role)
            .Select(x => x.Role)
            .ToArrayAsync(ct);

    /// <summary>Пользователь с его ролями из базы; <c>null</c> — ролей нет, значит не админ</summary>
    internal async Task<AdminDto?> Admin(UserDto user, string login, CancellationToken ct)
    {
        var roles = await RolesOf(user.Id, ct);

        if (roles.Length == 0) return null;

        return new AdminDto
        {
            UserId = user.Id,
            // Ника может не быть у того, кто входит по почте или телефону
            UserName = user.Profile.UserName ?? login,
            Roles = roles
        };
    }
}
