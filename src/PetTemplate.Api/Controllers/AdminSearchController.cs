using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Поиск в админке (спец. search, 5): полный реиндекс всех сущностей с текстом. Нужен,
/// когда сменилась модель эмбеддингов (новая коллекция пустая) и когда индекс впервые
/// строится поверх накопленных данных.
///
/// Реиндекс проходит по всем пространствам сразу — это не действие участника пространства,
/// поэтому он в админке, а роль — <c>AdminManagement</c>: уровень владельца инсталляции
/// </summary>
[Route("api/Admin/Search")]
[OutsideWorkspace]
[AuthorizeAdmin(AdminRole.AdminManagement)]
public class AdminSearchController(ApplicationContext db, ILogger<AdminSearchController> logger) : ControllerBase
{
    /// <summary>
    /// Ставит одну команду: обработчик сам разложит её на команды по сущности.
    /// Повторный вызов безвреден — индексация переписывает чанки целиком.
    /// Ответ значит «в очереди», а не «готово»
    /// </summary>
    [HttpPost("Reindex")]
    public async Task<IActionResult> Reindex(CancellationToken ct)
    {
        db.Enqueue(new SearchReindexCommand());
        await db.SaveChangesAsync(ct);

        // Кто это сделал — в лог: админка пишет автора всякого изменения (спец. admin, 3.5)
        logger.LogInformation("Search reindex requested by admin {AdminId}", User.GetUserId());

        return Ok();
    }
}
