using Microsoft.AspNetCore.Mvc;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Admin;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Аккаунты (спец. accounts, 8.5; admin, раздел 12): поиск, карточка, ссылка сброса,
/// освобождение почты и телефона, удаление аккаунта без пространств, журнал.
/// Всё — под ролью <c>Accounts</c>, и чтение тоже: карточка чужого аккаунта — не экран
/// «для любого админа»
/// </summary>
[Route("api/Admin/Accounts")]
[OutsideWorkspace]
[ValidateRequest]
[AuthorizeAdmin(AdminRole.Accounts)]
public class AdminAccountsController(AdminAccountService accounts) : ControllerBase
{
    [HttpGet]
    [Produces(typeof(AdminAccountDto[]))]
    public async Task<IActionResult> Search([FromQuery] string? query, CancellationToken ct) =>
        Ok(await accounts.Search(query, ct));

    /// <summary>
    /// Журнал: действия админов с аккаунтами и слияния, новые сверху, страницами. Здесь
    /// видно и то, что случилось с удалённым аккаунтом, — его карточка уже 404
    /// </summary>
    [HttpGet("Journal")]
    [Produces(typeof(AdminAccountJournalPageDto))]
    public async Task<IActionResult> Journal([FromQuery] AdminAccountJournalFilterDto filter, CancellationToken ct) =>
        Ok(await accounts.Journal(filter, ct));

    [HttpGet("{id:guid}")]
    [Produces(typeof(AdminAccountCardDto))]
    public async Task<IActionResult> Card(Guid id, CancellationToken ct) =>
        await accounts.Card(id, ct) is { } card ? Ok(card) : NotFound();

    [HttpPost("{id:guid}/Delete")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] AdminAccountReasonDto dto, CancellationToken ct) =>
        await accounts.Delete(id, dto.Reason, User.GetUserId(), ct) ? Ok() : NotFound();

    /// <summary>
    /// Ссылка сброса пароля (8.5). Ответ — ссылка: админ отправляет её человеку сам,
    /// PetTemplate её никуда не шлёт
    /// </summary>
    [HttpPost("{id:guid}/IssueReset")]
    [Produces(typeof(AdminIssuedResetDto))]
    public async Task<IActionResult> IssueReset(Guid id, [FromBody] AdminAccountReasonDto dto, CancellationToken ct) =>
        await accounts.IssueReset(id, dto.Reason, User.GetUserId(), ct) is { } issued ? Ok(issued) : NotFound();

    /// <summary>
    /// Освобождение почты (8.4, 8.5): <paramref name="id"/> — тот, кто просит; адрес
    /// снимается с аккаунта, где он вписан без подтверждения. Снятие и журнал — одной
    /// транзакцией
    /// </summary>
    [HttpPost("{id:guid}/ReleaseEmail")]
    [Produces(typeof(AdminReleasedContactDto))]
    public async Task<IActionResult> ReleaseEmail(Guid id, [FromBody] AdminReleaseEmailDto dto, CancellationToken ct) =>
        await accounts.ReleaseEmail(id, dto, User.GetUserId(), ct) is { } released ? Ok(released) : NotFound();

    /// <summary>
    /// Освобождение телефона (спец. accounts, 6): <paramref name="id"/> —
    /// тот, кто просит; номер снимается с аккаунта, где он вписан. Снятие и журнал — одной транзакцией
    /// </summary>
    [HttpPost("{id:guid}/ReleasePhone")]
    [Produces(typeof(AdminReleasedContactDto))]
    public async Task<IActionResult> ReleasePhone(Guid id, [FromBody] AdminReleasePhoneDto dto, CancellationToken ct) =>
        await accounts.ReleasePhone(id, dto, User.GetUserId(), ct) is { } released ? Ok(released) : NotFound();
}
