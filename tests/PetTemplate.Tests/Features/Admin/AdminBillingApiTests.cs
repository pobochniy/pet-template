using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// «Расход» в админке (спец. admin, 14; assistant, 13.5–13.6): сводку и карточку
/// пространства видит любой админ, запись в счёт поставщика и лимит — только роль
/// <c>Billing</c>; удалённое пространство остаётся в итогах
/// </summary>
public class AdminBillingApiTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly PetTemplate.Db.Entity.Profile _oleg = new UserBuilder("Oleg").Please().profile;

    private readonly PetTemplate.Db.Entity.Profile _vasya = new UserBuilder("Vasya").Please().profile;

    private readonly PetTemplate.Db.Entity.Workspace _workspace = new WorkspaceBuilder("Рога и Копыта").Please();

    private readonly AiModel _model = AssistantWorld.AiModel("Основная", 1, Now);

    /// <summary>
    /// Любой админ видит, во что обходится ассистент: ходы и служебные расходы,
    /// разрез по моделям и по пространствам — и удалённое пространство в нём тоже
    /// </summary>
    [Fact]
    public async Task AnyAdminSeesSummary()
    {
        // Arrange
        var gone = Guid.CreateVersion7();
        var (client, _) = await Client([AdminRole.Accounts], db =>
        {
            db.RunSpend.AddRange(Spend(_workspace.Id, _vasya.Id, 1000, 3m), Spend(gone, _vasya.Id, 500, 1m));
            db.ServiceSpend.Add(new ServiceSpend
            {
                Id = Guid.CreateVersion7(),
                Kind = ServiceSpendKind.Compaction,
                ModelId = _model.Id,
                Provider = _model.Provider,
                ModelKey = _model.Key,
                TokensIn = 200,
                Cost = 0.5m,
                CreatedUtc = Now
            });
        });

        // Act
        var response = await client.GetAsync("/api/Admin/Billing/Summary");

        // Assert
        await response.ShouldBeSuccessful();
        var summary = await response.Content.ReadFromJsonAsync<AdminBillingSummaryDto>();
        Assert.Equal(2, summary!.Runs);
        Assert.Equal(4m, summary.Cost);
        Assert.Equal(0.5m, summary.ServiceCost);
        Assert.Equal(4.5m, summary.TotalCost);
        Assert.Equal(1500, Assert.Single(summary.Models).Tokens);
        Assert.Equal(["Рога и Копыта", null], summary.Workspaces.Select(x => x.WorkspaceName));
        Assert.Equal("main", Assert.Single(summary.Providers).Provider);
    }

    /// <summary>Карточка пространства: действующий лимит, съеденное сегодня и кто сколько потратил</summary>
    [Fact]
    public async Task WorkspaceCardShowsLimitAndMembers()
    {
        // Arrange
        var (client, _) = await Client([AdminRole.Accounts], db =>
        {
            db.WorkspaceAssistantLimits.Add(new WorkspaceAssistantLimit
            {
                WorkspaceId = _workspace.Id,
                TokensPerDay = 10_000,
                UpdatedUtc = Now,
                UpdatedByUserId = _oleg.Id
            });
            db.RunSpend.AddRange(
                Spend(_workspace.Id, _vasya.Id, 1000, 3m),
                Spend(_workspace.Id, _vasya.Id, 500, 1m),
                Spend(_workspace.Id, _oleg.Id, 100, 0.1m));
        });

        // Act
        var response = await client.GetAsync($"/api/Admin/Billing/Workspaces/{_workspace.Id}");

        // Assert
        await response.ShouldBeSuccessful();
        var card = await response.Content.ReadFromJsonAsync<AdminBillingWorkspaceCardDto>();
        Assert.Equal(10_000, card!.OwnLimit!.TokensPerDay);
        Assert.Equal(10_000, card.Limit.TokensPerDay);
        Assert.Equal(1600, card.Today.Tokens);
        Assert.Equal(["Vasya", "Oleg"], card.Users.Select(x => x.Name));
        Assert.Equal(2, card.Users[0].Runs);
        Assert.Equal("Oleg", card.LimitUpdatedBy);
    }

    /// <summary>Свой лимит пространства ставит роль <c>Billing</c> — с отметкой, кто</summary>
    [Fact]
    public async Task BillingSetsWorkspaceLimit()
    {
        // Arrange
        var (client, db) = await Client([AdminRole.Billing]);

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Billing/Workspaces/{_workspace.Id}/Limit",
            new AdminAssistantLimitDto { TokensPerMonth = 5_000_000, CostPerDay = 100m });

        // Assert
        await response.ShouldBeSuccessful();
        var limit = await db.WorkspaceAssistantLimits.AsNoTracking().SingleAsync();
        Assert.Equal(5_000_000, limit.TokensPerMonth);
        Assert.Equal(100m, limit.CostPerDay);
        Assert.Null(limit.TokensPerDay);
        Assert.Equal(_oleg.Id, limit.UpdatedByUserId);
    }

    /// <summary>Сброс к умолчанию — строки своего лимита больше нет</summary>
    [Fact]
    public async Task BillingResetsWorkspaceLimit()
    {
        // Arrange
        var (client, db) = await Client([AdminRole.Billing], x => x.WorkspaceAssistantLimits.Add(
            new WorkspaceAssistantLimit { WorkspaceId = _workspace.Id, TokensPerDay = 1, UpdatedUtc = Now }));

        // Act
        var response = await client.DeleteAsync($"/api/Admin/Billing/Workspaces/{_workspace.Id}/Limit");

        // Assert
        await response.ShouldBeSuccessful();
        Assert.False(await db.WorkspaceAssistantLimits.AnyAsync());
    }

    /// <summary>Отрицательный потолок — отказ текстом; «без потолка» — пустое поле</summary>
    [Fact]
    public async Task NegativeLimitIsRefused()
    {
        // Arrange
        var (client, _) = await Client([AdminRole.Billing]);

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Billing/Workspaces/{_workspace.Id}/Limit",
            new AdminAssistantLimitDto { TokensPerDay = -1 });

        // Assert
        await response.ShouldBeBadRequest("Лимит не может быть отрицательным; без потолка — пустое поле");
    }

    /// <summary>Без роли <c>Billing</c> записывать нечего — <c>404</c>, как на любой отказ админки</summary>
    [Fact]
    public async Task WritesNeedBillingRole()
    {
        // Arrange
        var (client, db) = await Client([AdminRole.Accounts]);

        // Act
        var limit = await client.PutAsJsonAsync($"/api/Admin/Billing/Workspaces/{_workspace.Id}/Limit",
            new AdminAssistantLimitDto { TokensPerDay = 1 });
        var ledger = await client.PostAsJsonAsync("/api/Admin/Billing/Ledger",
            new AdminLedgerEntryDto { Provider = "main", Kind = ProviderLedgerKind.Payment, Amount = 100m });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, limit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ledger.StatusCode);
        Assert.False(await db.WorkspaceAssistantLimits.AnyAsync());
        Assert.False(await db.ProviderLedger.AnyAsync());
    }

    /// <summary>
    /// Счёт у поставщика: платёж минус расчёт за дни без факта — остаток; факт за прошедшие
    /// дни сверяется с расчётом ровно за свои дни
    /// </summary>
    [Fact]
    public async Task LedgerFeedsProviderAccount()
    {
        // Arrange
        var (client, _) = await Client([AdminRole.Billing], db =>
            db.RunSpend.Add(Spend(_workspace.Id, _vasya.Id, 1000, 30m)));
        var today = DateOnly.FromDateTime(Now);

        // Act
        await (await client.PostAsJsonAsync("/api/Admin/Billing/Ledger",
            new AdminLedgerEntryDto { Provider = "main", Kind = ProviderLedgerKind.Payment, Amount = 1000m }))
            .ShouldBeSuccessful();
        await (await client.PostAsJsonAsync("/api/Admin/Billing/Ledger", new AdminLedgerEntryDto
            {
                Provider = "main",
                Kind = ProviderLedgerKind.ActualSpend,
                Amount = 25m,
                PeriodFrom = today.AddDays(-10),
                PeriodTo = today.AddDays(-1)
            }))
            .ShouldBeSuccessful();
        var summary = await client.GetFromJsonAsync<AdminBillingSummaryDto>("/api/Admin/Billing/Summary");

        // Assert
        var provider = Assert.Single(summary!.Providers);
        Assert.Equal(25m, provider.Fact);
        // Расход сегодняшний — в дни факта не попал: расчёт за них нулевой
        Assert.Equal(0m, provider.Estimate);
        Assert.Equal(1000m, provider.Account.Paid);
        Assert.Equal(30m, provider.Account.Unbilled);
        Assert.Equal(1000m - 25m - 30m, provider.Account.Remaining);
    }

    private RunSpend Spend(Guid workspaceId, Guid userId, long tokens, decimal cost) => new()
    {
        Id = Guid.CreateVersion7(),
        RunId = Guid.CreateVersion7(),
        WorkspaceId = workspaceId,
        UserId = userId,
        ModelId = _model.Id,
        TokensIn = tokens,
        Cost = cost,
        CreatedUtc = Now
    };

    private async Task<(HttpClient client, ApplicationContext db)> Client(
        AdminRole[] roles,
        Action<ApplicationContext>? arrange = null) =>
        await Given.AdminClient(_oleg.Id, roles, db =>
        {
            db.Profiles.AddRange(_oleg, _vasya);
            db.Workspaces.Add(_workspace);
            db.AiModels.Add(_model);
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, roles));
            arrange?.Invoke(db);
        });
}
