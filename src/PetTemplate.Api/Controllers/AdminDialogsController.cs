using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Разбор диалогов (спец. admin, 16): список прогонов по признакам, трасса одного
/// и автооценка. Всё — под ролью <c>Dialogs</c>, самой тяжёлой в системе: это чужая
/// переписка. Каждое открытие трассы пишется в журнал доступа (<see cref="AdminAccessLogController"/>)
/// </summary>
[Route("api/Admin/Dialogs")]
[OutsideWorkspace]
[ValidateRequest]
[AuthorizeAdmin(AdminRole.Dialogs)]
public class AdminDialogsController(
    AdminDialogService dialogs,
    DialogEvaluationService evaluations,
    AdminJobService jobs,
    IOptions<AdminOptions> options) : ControllerBase
{
    [HttpGet]
    [Produces(typeof(AdminDialogPageDto))]
    public async Task<IActionResult> List([FromQuery] AdminDialogFilterDto filter, CancellationToken ct) =>
        Ok(await dialogs.List(filter, ct));

    /// <summary>
    /// Последние запуски автооценки (16.3) и потолок пачки — чтобы форма не предлагала
    /// того, от чего откажет сервер
    /// </summary>
    [HttpGet("Evaluations")]
    [Produces(typeof(AdminEvaluationsDto))]
    public async Task<IActionResult> Evaluations(CancellationToken ct) =>
        Ok(new AdminEvaluationsDto
        {
            MaxBatchSize = Math.Max(options.Value.Evaluation.BatchSize, 1),
            Jobs = await jobs.List(AdminJobKind.DialogEvaluation, ct)
        });

    /// <summary>
    /// «Оценить»: выбранный судья и размер пачки. Чужие судье диалоги пропускаются
    /// и считаются в запуске, а не подменяются моделью прогона. Отвечает запуском
    /// </summary>
    [HttpPost("Evaluations")]
    [Produces(typeof(AdminJobDto))]
    public async Task<IActionResult> Evaluate([FromBody] AdminEvaluationStartDto dto, CancellationToken ct)
    {
        var jobId = await evaluations.Start(dto, User.GetUserId(), ct);
        return Ok(await jobs.Get(jobId, AdminJobKind.DialogEvaluation, ct));
    }

    /// <summary>Трасса старше срока хранения — не ошибка, а <c>Expired</c> в ответе</summary>
    [HttpGet("{runId:guid}")]
    [Produces(typeof(AdminDialogTraceDto))]
    public async Task<IActionResult> Open(Guid runId, CancellationToken ct) =>
        await dialogs.Open(runId, User.GetUserId(), ct) is { } trace ? Ok(trace) : NotFound();
}
