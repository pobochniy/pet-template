using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Админская роль пользователя (спец. admin, 3.1 и 5) — устроено как
/// <see cref="UserInPermission"/>: строка на пару «человек + роль», отдельной
/// сущности «администратор» нет. Нет строк — нет админа.
///
/// Первого админа заводит владелец строками руками, по одной на каждую роль,
/// с <see cref="GrantedByUserId"/> = <c>null</c> (<c>IaC/DEPLOYMENT.md</c>, «Админка»).
/// Ни сида, ни переменной окружения под это нет намеренно: дверь, которая открывается
/// сама при пустой таблице, объяснить труднее, чем один раз вставить строки (3.1).
/// В базе — <c>user_in_admin_roles (user_id, role, granted_utc, granted_by_user_id)</c>
/// </summary>
public class UserInAdminRole
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Ровно одна роль — флаговость <see cref="AdminRole"/> нужна приглашению</summary>
    public AdminRole Role { get; set; }

    public DateTime GrantedUtc { get; set; }

    /// <summary>
    /// Кто выдал. <c>null</c> — у первого админа, заведённого строкой в базе,
    /// и у того, чей выдавший ушёл из системы: ссылка пустеет, роль остаётся
    /// </summary>
    public Guid? GrantedByUserId { get; set; }
}

public class UserInAdminRoleConfiguration : IEntityTypeConfiguration<UserInAdminRole>
{
    public void Configure(EntityTypeBuilder<UserInAdminRole> builder)
    {
        builder.HasKey(x => new { x.UserId, x.Role });

        // Удаление пользователя уносит и его роли (3.1). Сам путь удаления аккаунта
        // (AccountDeletion) аккаунт с ролью не удаляет (спец. accounts, 10): каскад здесь —
        // для удаления строкой в базе. Если так уходит последний AdminManagement — чинится
        // строкой в базе, как и заводилось
        builder
            .HasOne(x => x.User)
            .WithMany(x => x.AdminRoles)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Роль переживает выдавшего — ссылка пустеет (5)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.GrantedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
