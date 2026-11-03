using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Middlewares;
using PetTemplate.Shared.Dto.Admin;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// «Сводка» — первый экран админки (спец. admin, 4 и 6). Открыта всякому,
/// кто вошёл: роль у раздела — <c>null</c> (3.2)
/// </summary>
[Route("api/Admin")]
[OutsideWorkspace]
public class AdminSummaryController : ControllerBase
{
    /// <summary>
    /// Версия сборки API и время сервера. Версия — из сборки API, а не из входной:
    /// под тестами входная сборка — раннер
    /// </summary>
    [HttpGet("Summary")]
    [AuthorizeAdmin]
    [Produces(typeof(AdminSummaryDto))]
    public IActionResult Summary()
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? string.Empty;

        return Ok(new AdminSummaryDto
        {
            Version = version,
            ServerTimeUtc = DateTime.UtcNow
        });
    }
}
