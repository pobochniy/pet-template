using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// «Расход» (спец. admin, 14; assistant, 13.5–13.6): сводка «расчёт / факт», счёт
/// у поставщика и лимиты ассистента у пространств. Смотреть — любому админу: расчёты
/// по затратам ролью не закрываются, а имён пространств и логинов здесь не больше,
/// чем в «Аккаунтах». Записывать — роли <c>Billing</c>: запись в счёт двигает главную
/// строку сводки, а лимит — потолок у живых людей
/// </summary>
[Route("api/Admin/Billing")]
[OutsideWorkspace]
[ValidateRequest]
public class AdminBillingController(AdminBillingService billing) : ControllerBase
{
    /// <param name="from">Первый день по UTC; по умолчанию — 30 дней до <paramref name="to"/></param>
    /// <param name="to">Последний день по UTC, включительно; по умолчанию — сегодня</param>
    /// <param name="modelId">Одна модель вместо всех</param>
    [HttpGet("Summary")]
    [AuthorizeAdmin]
    [Produces(typeof(AdminBillingSummaryDto))]
    public async Task<IActionResult> Summary(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? modelId,
        CancellationToken ct) =>
        Ok(await billing.Summary(from, to, modelId, ct));

    /// <summary>Платёж поставщику или факт расхода за период; правка факта — тем же периодом</summary>
    [HttpPost("Ledger")]
    [AuthorizeAdmin(AdminRole.Billing)]
    [Produces(typeof(Guid))]
    public async Task<IActionResult> Record([FromBody] AdminLedgerEntryDto dto, CancellationToken ct) =>
        Ok(await billing.Record(dto, User.GetUserId(), ct));

    /// <summary>Лимит пространства, сколько съедено сейчас и кто сколько потратил за период</summary>
    [HttpGet("Workspaces/{id:guid}")]
    [AuthorizeAdmin]
    [Produces(typeof(AdminBillingWorkspaceCardDto))]
    public async Task<IActionResult> Workspace(
        Guid id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct) =>
        await billing.Workspace(id, from, to, ct) is { } card ? Ok(card) : NotFound();

    /// <summary>Свой лимит пространства целиком — вместо умолчания из конфигурации</summary>
    [HttpPut("Workspaces/{id:guid}/Limit")]
    [AuthorizeAdmin(AdminRole.Billing)]
    public async Task<IActionResult> SetLimit(Guid id, [FromBody] AdminAssistantLimitDto dto, CancellationToken ct) =>
        await billing.SetLimit(id, dto, User.GetUserId(), ct) ? Ok() : NotFound();

    /// <summary>Вернуть пространство к умолчанию</summary>
    [HttpDelete("Workspaces/{id:guid}/Limit")]
    [AuthorizeAdmin(AdminRole.Billing)]
    public async Task<IActionResult> ResetLimit(Guid id, CancellationToken ct) =>
        await billing.ResetLimit(id, ct) ? Ok() : NotFound();
}
