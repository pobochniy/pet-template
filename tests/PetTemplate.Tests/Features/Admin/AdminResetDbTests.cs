using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Asserts;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Ссылка сброса пароля от поддержки (спец. accounts, 8.2 и 8.5): токен со сроком
/// <c>AdminResetTokenTtl</c> и запись в журнал — одним сохранением; перевыдача гасит
/// прежнюю и срока не продлевает. На базе: строка пользователя запирается
/// </summary>
public class AdminResetDbTests
{
    private const string Reason = "Написала в личку: забыла пароль, назвала участников пространства";

    private const string NewPassword = "Novyi-parol-42";

    private readonly AdminAccountsWorld _world = new();

    private Task<(HttpClient client, ApplicationContext db)> Client(Action<ApplicationContext>? extra = null) =>
        _world.Client(extra);

    private static Task<HttpResponseMessage> Issue(HttpClient client, Guid userId, string? reason = Reason) =>
        client.PostAsJsonAsync($"/api/Admin/Accounts/{userId}/IssueReset", new AdminAccountReasonDto { Reason = reason });

    [Fact]
    public async Task IssuesLinkForADay()
    {
        var (client, db) = await Client();

        var response = await Issue(client, _world.Masha.Id);

        await response.ShouldBeSuccessful();
        var issued = await response.Content.ReadFromJsonAsync<AdminIssuedResetDto>();
        Assert.False(issued!.Replaced);
        var raw = issued.Url.Split("/auth/reset/")[1];
        var token = await db.AccountTokens.AsNoTracking().SingleAsync();
        Assert.Equal(AccountTokens.HashOf(raw), token.Hash);
        Assert.Equal(_world.Admin.Id, token.IssuedByUserId);
        Assert.Equal(TimeSpan.FromHours(24), token.ExpiresAtUtc - token.CreatedAtUtc);
        var action = await db.AccountAdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(AccountAdminActionKind.IssueReset, action.Kind);
        Assert.Equal("Masha", action.TargetUserName);
        Assert.Equal(Reason, action.Reason);
    }

    /// <summary>По ссылке задаётся новый пароль, и с ним входят (8.2)</summary>
    [Fact]
    public async Task LinkResetsPassword()
    {
        var (client, _) = await Client();
        var issued = await (await Issue(client, _world.Masha.Id)).Content.ReadFromJsonAsync<AdminIssuedResetDto>();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsJsonAsync("/api/Auth/Reset", new ResetPasswordDto
        {
            Token = issued!.Url.Split("/auth/reset/")[1],
            NewPassword = NewPassword,
            NewPasswordConfirm = NewPassword
        });

        await response.ShouldBeSuccessful();
        var login = await client.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto { Login = "Masha", Password = NewPassword });
        await login.ShouldBeSuccessful();
    }

    /// <summary>
    /// Пока выданная ссылка жива, повторная выдача — новая ссылка взамен прежней с прежним
    /// сроком: перевыдачей окно не продлить (8.5)
    /// </summary>
    [Fact]
    public async Task ReissueWhileAliveKeepsExpiry()
    {
        var (client, db) = await Client();
        var first = await (await Issue(client, _world.Masha.Id)).Content.ReadFromJsonAsync<AdminIssuedResetDto>();

        var response = await Issue(client, _world.Masha.Id);

        await response.ShouldBeSuccessful();
        var second = await response.Content.ReadFromJsonAsync<AdminIssuedResetDto>();
        Assert.True(second!.Replaced);
        Assert.NotEqual(first!.Url, second.Url);
        // Первый срок — из ответа, второй — из базы: Postgres хранит до микросекунды
        Assert.InRange(second.ExpiresAtUtc - first.ExpiresAtUtc, TimeSpan.FromMilliseconds(-1), TimeSpan.FromMilliseconds(1));
        var tokens = await db.AccountTokens.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.NotNull(tokens[0].RevokedAtUtc);
        Assert.Null(tokens[1].RevokedAtUtc);
        Assert.Equal(2, await db.AccountAdminActions.CountAsync());
    }

    /// <summary>Ссылкой воспользовались — следующая выдача уже новая, с полным сроком</summary>
    [Fact]
    public async Task NewFullTermAfterLinkWasUsed()
    {
        var (client, db) = await Client();
        await (await Issue(client, _world.Masha.Id)).ShouldBeSuccessful();
        var now = DateTime.UtcNow;
        await db.AccountTokens.ExecuteUpdateAsync(x => x.SetProperty(t => t.UsedAtUtc, now.AddHours(-1))
            .SetProperty(t => t.ExpiresAtUtc, now.AddHours(1)));

        var response = await Issue(client, _world.Masha.Id);

        await response.ShouldBeSuccessful();
        var issued = await response.Content.ReadFromJsonAsync<AdminIssuedResetDto>();
        Assert.False(issued!.Replaced);
        var token = await db.AccountTokens.AsNoTracking().SingleAsync(x => x.UsedAtUtc == null);
        Assert.Equal(TimeSpan.FromHours(24), token.ExpiresAtUtc - token.CreatedAtUtc);
    }

    /// <summary>Две одновременные выдачи — одна живая ссылка: строка пользователя заперта</summary>
    [Fact]
    public async Task ConcurrentIssuesLeaveOneLiveLink()
    {
        var (client, db) = await Client();

        var responses = await Task.WhenAll(Issue(client, _world.Masha.Id), Issue(client, _world.Masha.Id));

        Assert.All(responses, x => Assert.True(x.IsSuccessStatusCode));
        Assert.Single(await db.AccountTokens.AsNoTracking().Where(x => x.RevokedAtUtc == null).ToListAsync());
    }

    /// <summary>
    /// Аккаунту с ролью в админке ссылку не выдают — как и не удаляют (раздел 10): иначе
    /// <c>Accounts</c> выдаст себе вход под админом с большими правами
    /// </summary>
    [Fact]
    public async Task RefusesForAdminAccount()
    {
        var (client, db) = await Client(x => x.UserInAdminRoles.Add(new UserInAdminRole
        {
            UserId = _world.Masha.Id, Role = AdminRole.AdminManagement, GrantedUtc = DateTime.UtcNow
        }));

        var response = await Issue(client, _world.Masha.Id);

        await response.ShouldBeBadRequestWith(AuthMessages.AdminCannotBeDeleted);
        Assert.Empty(await db.AccountTokens.AsNoTracking().ToListAsync());
        Assert.Empty(await db.AccountAdminActions.AsNoTracking().ToListAsync());
    }

    /// <summary>Аккаунт без пространств не восстанавливают — его удаляют (8.5)</summary>
    [Fact]
    public async Task RefusesWithoutWorkspaces()
    {
        var (client, db) = await Client();

        var response = await Issue(client, _world.Lonely.Id);

        await response.ShouldBeBadRequest("У аккаунта нет пространств — восстанавливать нечего, такой аккаунт удаляют");
        Assert.Empty(await db.AccountTokens.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task NeedsReason()
    {
        var (client, db) = await Client();

        var response = await Issue(client, _world.Masha.Id, reason: "");

        await response.ShouldBeBadRequest("Укажите причину");
        Assert.Empty(await db.AccountTokens.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task MissingAccountIsNotFound()
    {
        var (client, _) = await Client();

        var response = await Issue(client, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
