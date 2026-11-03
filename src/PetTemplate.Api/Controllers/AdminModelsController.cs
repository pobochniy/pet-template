using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Справочник моделей (спец. admin, 15): читать — любому админу, править — роли
/// <c>Models</c>. Удаления нет, только выключение
/// </summary>
[Route("api/Admin/Models")]
[OutsideWorkspace]
[ValidateRequest]
public class AdminModelsController(AdminModelService models, ModelCheckService checks, AdminJobService jobs)
    : ControllerBase
{
    [HttpGet]
    [AuthorizeAdmin]
    [Produces(typeof(AdminModelsDto))]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await models.List(ct));

    [HttpPost]
    [AuthorizeAdmin(AdminRole.Models)]
    [Produces(typeof(Guid))]
    public async Task<IActionResult> Create([FromBody] AdminModelEditDto dto, CancellationToken ct) =>
        Ok(await models.Create(dto, User.GetUserId(), ct));

    /// <summary>Правка строки; поменяли её за это время — отказ «откройте заново» (15.1)</summary>
    [HttpPut("{id:guid}")]
    [AuthorizeAdmin(AdminRole.Models)]
    public async Task<IActionResult> Update(Guid id, [FromBody] AdminModelEditDto dto, CancellationToken ct) =>
        await models.Update(id, dto, User.GetUserId(), ct) ? Ok() : NotFound();

    /// <summary>
    /// Замкнуть вручную, не дожидаясь конца паузы размыкателя (спец. assistant, 16.2):
    /// модель ожила, а ждать незачем
    /// </summary>
    [HttpPost("{id:guid}/Close")]
    [AuthorizeAdmin(AdminRole.Models)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) =>
        await models.Close(id, ct) ? Ok() : NotFound();

    /// <summary>
    /// Сверить модель набором фраз (15.3) — в том числе выключенную: кандидата проверяют
    /// до включения. Идёт фоном; отвечает запуском, который экран перечитывает
    /// </summary>
    [HttpPost("{id:guid}/Check")]
    [AuthorizeAdmin(AdminRole.Models)]
    [Produces(typeof(AdminJobDto))]
    public async Task<IActionResult> Check(Guid id, CancellationToken ct)
    {
        var jobId = await checks.Start(id, User.GetUserId(), ct);
        return Ok(await jobs.Get(jobId, AdminJobKind.ModelCheck, ct));
    }

    /// <summary>Последние сверки — без отчётов: отчёт весит десятки килобайт</summary>
    [HttpGet("Checks")]
    [AuthorizeAdmin]
    [Produces(typeof(AdminJobDto[]))]
    public async Task<IActionResult> Checks(CancellationToken ct) =>
        Ok(await jobs.List(AdminJobKind.ModelCheck, ct));

    /// <summary>Сверка с отчётом на сейчас: пока идёт — сделанные фразы, по готовности — все</summary>
    [HttpGet("Checks/{jobId:guid}")]
    [AuthorizeAdmin]
    [Produces(typeof(AdminJobDto))]
    public async Task<IActionResult> CheckReport(Guid jobId, CancellationToken ct) =>
        await jobs.Get(jobId, AdminJobKind.ModelCheck, ct) is { } job ? Ok(job) : NotFound();

    /// <summary>Порядок целиком: все модели справочника в новом порядке (15.2)</summary>
    [HttpPut("Order")]
    [AuthorizeAdmin(AdminRole.Models)]
    public async Task<IActionResult> Reorder([FromBody] Guid[] ids, CancellationToken ct)
    {
        await models.Reorder(ids, User.GetUserId(), ct);
        return Ok();
    }
}
