using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Журнал доступа к диалогам (спец. admin, 16.2) — под <c>AdminManagement</c>, а не
/// под <c>Dialogs</c>: смысл записи в том, что её смотрит не тот, кто её оставил.
/// Только чтение — удалить запись нечем, кроме срока хранения
/// </summary>
[Route("api/Admin/AccessLog")]
[OutsideWorkspace]
[AuthorizeAdmin(AdminRole.AdminManagement)]
public class AdminAccessLogController(AdminAccessLogService accessLog) : ControllerBase
{
    [HttpGet]
    [Produces(typeof(AdminAccessLogPageDto))]
    public async Task<IActionResult> List([FromQuery] AdminAccessLogFilterDto filter, CancellationToken ct) =>
        Ok(await accessLog.List(filter, ct));
}
