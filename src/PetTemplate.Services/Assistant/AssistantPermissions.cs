using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Права для инструментов ассистента (спец. assistant, раздел 9).
///
/// В шаблоне авторизация живёт в контроллере, атрибутом <c>[AuthorizePermissions]</c>,
/// а сервисы про права не знают вовсе (<c>src/PetTemplate.Api/agents.md</c>). Инструмент
/// зовёт сервис раздела напрямую и тем самым <b>обходит атрибут</b> — человек с одним
/// правом на чтение создавал бы записи через ассистента. Поэтому проверка повторяется
/// здесь, тем же набором, что стоит на соответствующем эндпоинте раздела.
///
/// Читаем живые данные, а не набор из токена или снимок из прогона: право отбирают
/// и между показом плана и словом «да» (5.4). Кэша здесь нет по той же причине.
/// </summary>
public class AssistantPermissions(ApplicationContext context)
{
    /// <summary>
    /// Есть ли у человека в этом пространстве хотя бы одно из перечисленных прав —
    /// ровно та же семантика, что у атрибута на контроллере. Пустой набор — «участник
    /// пространства», как <c>[AuthorizePermissions]</c> без аргументов
    /// </summary>
    public async Task<bool> Has(Guid userId, Guid workspaceId, CancellationToken ct, params PermissionEnum[] anyOf)
    {
        if (anyOf.Length == 0) return await IsMember(userId, workspaceId, ct);

        return await context.UserInPermissions
            .AnyAsync(x => x.WorkspaceId == workspaceId
                           && x.UserId == userId
                           && anyOf.Contains(x.PermissionId), ct);
    }

    /// <summary>
    /// Состоит ли человек в пространстве. Состав пространства — это строки
    /// <c>user_in_permissions</c> (спец. authorization, 1.1), поэтому отдельной таблицы
    /// участников не спрашиваем.
    ///
    /// Нужно там, где прав не спрашивают, а спрашивают «он вообще ещё здесь»:
    /// автора хода могли убрать из пространства между показом плана и словом «да» (5.4)
    /// </summary>
    public async Task<bool> IsMember(Guid userId, Guid workspaceId, CancellationToken ct) =>
        await context.UserInPermissions
            .AnyAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct);
}
