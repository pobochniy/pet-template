using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using PetTemplate.Api.Extensions.Auth;

namespace PetTemplate.Tests.Features.Account;

/// <summary>
/// Слияние аккаунтов (спец. accounts, раздел 7): удаляемый уходит общим путём удаления,
/// а его держат внешние ключи в базе; замок строк и <c>409</c> — тоже только там.
///
/// Олег вошёл паролем. Его второй аккаунт — «tg555» из Telegram, заведён два дня назад:
/// в «Рогах и Копытах» он владелец со всеми правами (у Олега там только чтение состава),
/// в «Пончиках и Плюшках» — участник у Пети, Олега там нет, и там висит выданное им
/// приглашение; «Шарики и Палочки» — только его. Основное пространство Олега — «Рога»
/// </summary>
public class AccountMergeTests
{
    private const long SecondTelegram = 555000555;

    private readonly PetTemplate.Db.Entity.Workspace _roga = new WorkspaceBuilder("Рога и Копыта").Please();
    private readonly PetTemplate.Db.Entity.Workspace _ponchiki = new WorkspaceBuilder("Пончики и Плюшки").Please();
    private readonly PetTemplate.Db.Entity.Workspace _shariki = new WorkspaceBuilder("Шарики и Палочки").Please();
    private readonly string _olegToken;
    private readonly PetTemplate.Db.Entity.Profile _oleg;
    private readonly PetTemplate.Db.Entity.Profile _second;
    private readonly PetTemplate.Db.Entity.Profile _petya;
    private readonly WorkspaceInvitation _secondInvitation;

    public AccountMergeTests()
    {
        (_olegToken, _oleg) = new UserBuilder("Oleg")
            .WithoutTelegram()
            .WithPermissions(_roga.Id, PermissionEnum.WorkspaceMemberRead)
            .WithPrimaryWorkspace(_roga.Id)
            .Please();
        (_, _second) = new UserBuilder("tg555", telegramId: SecondTelegram)
            .WithoutPassword()
            .WithAllPermissions(_roga.Id)
            .WithPermissions(_ponchiki.Id, PermissionEnum.WorkspaceMemberRead, PermissionEnum.WorkspaceMemberCrud)
            .WithAllPermissions(_shariki.Id)
            .WithPrimaryWorkspace(_shariki.Id)
            .Please();
        _second.User!.CreatedAtUtc = DateTime.UtcNow.AddDays(-2);
        (_, _petya) = new UserBuilder("Petya").WithAllPermissions(_ponchiki.Id).Please();
        _secondInvitation = new WorkspaceInvitationBuilder(_ponchiki.Id, _second.Id).Please();
    }

    private void World(ApplicationContext x)
    {
        x.Workspaces.AddRange(_roga, _ponchiki, _shariki);
        x.Profiles.AddRange(_oleg, _second, _petya);
        x.WorkspaceInvitations.Add(_secondInvitation);
    }

    private static MergeProofDto TelegramProof(long telegramId) => new()
    {
        External = new ExternalProofDto
        {
            Provider = ExternalProvider.Telegram,
            Code = new TelegramLoginBuilder().WithUserId(telegramId).Build(),
            State = TelegramLoginBuilder.DefaultNonce
        }
    };

    private static MergeProofDto PasswordProof(string login) => new() { Login = login, Password = UserBuilder.SuperAdminPassword };

    private Task<(HttpClient client, ApplicationContext db)> Client(Action<ApplicationContext>? extra = null) =>
        Given.ApiClient(x =>
        {
            World(x);
            extra?.Invoke(x);
        }, _olegToken);

    private static async Task<MergePreviewDto> Preview(HttpClient client, MergeProofDto proof)
    {
        var response = await client.PostAsJsonAsync("/api/Account/MergePreview", proof);
        await response.ShouldBeSuccessful();
        return (await response.Content.ReadFromJsonAsync<MergePreviewDto>())!;
    }

    private static Task<HttpResponseMessage> Merge(HttpClient client, MergePreviewDto preview, Guid keepUserId) =>
        client.PostAsJsonAsync("/api/Account/Merge", new MergeDto { Ticket = preview.Ticket, KeepUserId = keepUserId });

    /// <summary>
    /// Слить, оставив Олега. Владение вторым — входом Telegram, как в основном сценарии:
    /// пароля у аккаунта из Telegram нет
    /// </summary>
    private async Task<(HttpClient client, ApplicationContext db, AuthenticationResultDto session)> MergeKeepingOleg(
        Action<ApplicationContext>? extra = null)
    {
        var (client, db) = await Client(extra);
        var response = await Merge(client, await Preview(client, TelegramProof(SecondTelegram)), _oleg.Id);
        await response.ShouldBeSuccessful();
        db.ChangeTracker.Clear();
        return (client, db, (await response.Content.ReadFromJsonAsync<AuthenticationResultDto>())!);
    }

    [Fact]
    public async Task PreviewShowsBothAccounts()
    {
        var (client, _) = await Client();

        var preview = await Preview(client, TelegramProof(SecondTelegram));

        Assert.Equal(_oleg.Id, preview.Current.UserId);
        Assert.Equal(_second.Id, preview.Other.UserId);
        Assert.True(preview.Other.HasTelegram);
        Assert.False(preview.Other.HasPassword);
        Assert.True(preview.CanKeepOther);
        Assert.True(preview.Other.Workspaces.Single(x => x.Id == _shariki.Id).IsSolo);
        var roga = preview.Other.Workspaces.Single(x => x.Id == _roga.Id);
        Assert.True(roga.IsOwner);
        Assert.Equal(["Oleg"], roga.OtherMembers);
    }

    /// <summary>
    /// Второй заведён меньше суток назад — остаться может только текущий: украденная сессия
    /// иначе сливала бы аккаунт жертвы в свежий аккаунт атакующего (7.3)
    /// </summary>
    [Fact]
    public async Task CannotKeepYoungOtherAccount()
    {
        _second.User!.CreatedAtUtc = DateTime.UtcNow.AddHours(-1);
        var (client, db) = await Client();
        var preview = await Preview(client, TelegramProof(SecondTelegram));

        var response = await Merge(client, preview, _second.Id);

        Assert.False(preview.CanKeepOther);
        await response.ShouldBeBadRequestWith(AuthMessages.MergeKeepYoungAccount);
        Assert.True(await db.Users.AsNoTracking().AnyAsync(x => x.Id == _oleg.Id));
    }

    /// <summary>Остающийся — уже участник: права объединяются, слияние прав не отнимает (7.2.1)</summary>
    [Fact]
    public async Task RightsAreUnited()
    {
        var (_, db, _) = await MergeKeepingOleg();

        var rights = await db.UserInPermissions.Where(x => x.UserId == _oleg.Id && x.WorkspaceId == _roga.Id)
            .Select(x => x.PermissionId).ToListAsync();
        Assert.Equal(Enum.GetValues<PermissionEnum>().Length - 1, rights.Count);
    }

    /// <summary>Членство в общем пространстве, где остающегося не было, переходит к нему с теми же правами</summary>
    [Fact]
    public async Task MembershipMoves()
    {
        var (_, db, _) = await MergeKeepingOleg();

        var rights = await db.UserInPermissions.Where(x => x.UserId == _oleg.Id && x.WorkspaceId == _ponchiki.Id)
            .Select(x => x.PermissionId).OrderBy(x => x).ToListAsync();
        Assert.Equal([PermissionEnum.WorkspaceMemberCrud, PermissionEnum.WorkspaceMemberRead], rights);
    }

    /// <summary>Основное пространство остающегося не меняется (7.2.1)</summary>
    [Fact]
    public async Task PrimaryWorkspaceOfKeptStays()
    {
        var (_, db, _) = await MergeKeepingOleg();

        Assert.Equal(_roga.Id, (await db.Profiles.SingleAsync(x => x.Id == _oleg.Id)).PrimaryWorkspaceId);
    }

    /// <summary>Основного не было — им становится перешедшее: открывать при входе есть что</summary>
    [Fact]
    public async Task KeptWithoutPrimaryGetsMovedOne()
    {
        _oleg.PrimaryWorkspaceId = null;

        var (_, db, _) = await MergeKeepingOleg();

        var primary = (await db.Profiles.SingleAsync(x => x.Id == _oleg.Id)).PrimaryWorkspaceId;
        Assert.Contains(primary!.Value, new[] { _roga.Id, _ponchiki.Id });
    }

    [Fact]
    public async Task SoloWorkspaceIsDeleted()
    {
        var (_, db, _) = await MergeKeepingOleg();

        Assert.False(await db.Workspaces.AnyAsync(x => x.Id == _shariki.Id));
        Assert.True(await db.Workspaces.AnyAsync(x => x.Id == _ponchiki.Id));
    }

    /// <summary>
    /// Авторство на остающегося не перебивается (7.2.1): приглашение удаляемого остаётся
    /// живым, но без автора — как после любого удаления (раздел 10)
    /// </summary>
    [Fact]
    public async Task InvitationAuthorshipIsNotMoved()
    {
        var (_, db, _) = await MergeKeepingOleg();

        Assert.Null((await db.WorkspaceInvitations.SingleAsync(x => x.Id == _secondInvitation.Id)).CreatedBy);
    }

    /// <summary>Второй аккаунт удалён физически; его Telegram теперь ведёт в Олега</summary>
    [Fact]
    public async Task SecondAccountIsGoneAndTelegramMoves()
    {
        var (_, db, _) = await MergeKeepingOleg();

        Assert.False(await db.Users.AnyAsync(x => x.Id == _second.Id));
        Assert.Equal(SecondTelegram, (await db.Users.SingleAsync(x => x.Id == _oleg.Id)).TelegramId);
    }

    /// <summary>
    /// Telegram, переехавший слиянием, — свежепривязанный: сутки он не задаёт пароль
    /// без текущего (8.3)
    /// </summary>
    [Fact]
    public async Task MovedLoginCountsAsRecentlyLinked()
    {
        var (_, db, _) = await MergeKeepingOleg();

        var oleg = await db.Users.AsNoTracking().SingleAsync(x => x.Id == _oleg.Id);
        Assert.Equal(SecondTelegram, oleg.TelegramId);
        Assert.InRange(DateTime.UtcNow - oleg.TelegramLinkedAtUtc!.Value, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    /// <summary>Владение доказано входом Telegram — так и записано в журнал (7.4)</summary>
    [Fact]
    public async Task MergeIsJournaled()
    {
        var (_, db, _) = await MergeKeepingOleg();

        var merge = await db.AccountMerges.SingleAsync();
        Assert.Equal(_oleg.Id, merge.KeptUserId);
        Assert.Equal("Oleg", merge.KeptUserName);
        Assert.Equal(_second.Id, merge.RemovedUserId);
        Assert.Equal("tg555", merge.RemovedUserName);
        Assert.Equal("telegram", merge.Platform);
    }

    /// <summary>Ответ — новая сессия остающегося: в ней права перешедшего членства</summary>
    [Fact]
    public async Task AnswersWithNewSessionOfKept()
    {
        var (_, _, session) = await MergeKeepingOleg();

        Assert.Equal(_oleg.Id, session.Profile.Id);
        Assert.Contains(_ponchiki.Id, session.PermissionsByWorkspace.Keys);
        Assert.DoesNotContain(_shariki.Id, session.PermissionsByWorkspace.Keys);
    }

    /// <summary>
    /// Остаётся второй — тот, из которого не начинали: сессия текущего больше не действует,
    /// клиент живёт дальше токеном из ответа
    /// </summary>
    [Fact]
    public async Task KeepingOtherEndsCurrentSession()
    {
        var (client, _) = await Client();
        var merged = await Merge(client, await Preview(client, TelegramProof(SecondTelegram)), _second.Id);
        await merged.ShouldBeSuccessful();
        Assert.Equal(_second.Id, (await merged.Content.ReadFromJsonAsync<AuthenticationResultDto>())!.Profile.Id);

        var response = await client.GetAsync("/api/Account/Logins");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Состав пространств поменялся между просмотром и подтверждением — 409 (7.4)</summary>
    [Fact]
    public async Task ChangedCompositionConflicts()
    {
        var (client, db) = await Client();
        var preview = await Preview(client, TelegramProof(SecondTelegram));
        db.UserInPermissions.Add(new UserInPermission
        {
            WorkspaceId = _shariki.Id, UserId = _petya.Id, PermissionId = PermissionEnum.WorkspaceMemberRead
        });
        await db.SaveChangesAsync();

        var response = await Merge(client, preview, _oleg.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await db.Users.AsNoTracking().AnyAsync(x => x.Id == _second.Id));
    }

    /// <summary>
    /// Два подтверждения одного слияния одновременно: замок строк пропускает одно, второе
    /// застаёт удаляемого удалённым — 409, а не 500 и не двойное удаление. Второй ключ —
    /// по паролю: подписанный вход Telegram одноразовый
    /// </summary>
    [Fact]
    public async Task SimultaneousMergesConflict()
    {
        _second.User!.PasswordHash = _oleg.User!.PasswordHash;
        _second.User.SecurityStamp = _oleg.User.SecurityStamp;
        var (client, db) = await Client();
        var first = await Preview(client, TelegramProof(SecondTelegram));
        var second = await Preview(client, PasswordProof("tg555"));

        var responses = await Task.WhenAll(Merge(client, first, _oleg.Id), Merge(client, second, _oleg.Id));

        Assert.Single(responses, x => x.IsSuccessStatusCode);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.AccountMerges.CountAsync());
    }

    /// <summary>Ключ одноразовый</summary>
    [Fact]
    public async Task TicketWorksOnce()
    {
        var (client, _) = await Client();
        var preview = await Preview(client, TelegramProof(SecondTelegram));
        await (await Merge(client, preview, _oleg.Id)).ShouldBeSuccessful();

        var response = await Merge(client, preview, _oleg.Id);

        await response.ShouldBeBadRequestWith(AuthMessages.MergeTicketInvalid);
    }

    /// <summary>Ключ просмотра выдан Олегу — оставить по нему третий аккаунт нельзя</summary>
    [Fact]
    public async Task KeepingStrangerIsRefused()
    {
        var (client, db) = await Client();
        var preview = await Preview(client, TelegramProof(SecondTelegram));

        var response = await Merge(client, preview, _petya.Id);

        await response.ShouldBeBadRequestWith(AuthMessages.MergeTicketInvalid);
        Assert.True(await db.Users.AsNoTracking().AnyAsync(x => x.Id == _second.Id));
    }

    /// <summary>Неверная пара — 422, как у входа, а не 401: тот выбросил бы из своей сессии</summary>
    [Fact]
    public async Task WrongPasswordProofIsRefused()
    {
        var (client, _) = await Client();

        var response = await client.PostAsJsonAsync("/api/Account/MergePreview",
            new MergeProofDto { Login = "Petya", Password = "ne-tot-parol" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>По паролю второго — тот же просмотр, в журнал уйдёт «password»</summary>
    [Fact]
    public async Task PasswordProofOpensPreview()
    {
        var (client, _) = await Client();

        var preview = await Preview(client, PasswordProof("Petya"));

        Assert.Equal(_petya.Id, preview.Other.UserId);
    }

    [Fact]
    public async Task SameAccountIsRefused()
    {
        var (client, _) = await Client();

        var response = await client.PostAsJsonAsync("/api/Account/MergePreview", PasswordProof("Oleg"));

        await response.ShouldBeBadRequestWith(AuthMessages.MergeSameAccount);
    }

    /// <summary>Вход площадки ничей — сливать не с чем, его можно просто привязать</summary>
    [Fact]
    public async Task UnknownExternalIsRefused()
    {
        var (client, _) = await Client();

        var response = await client.PostAsJsonAsync("/api/Account/MergePreview", TelegramProof(777000777));

        await response.ShouldBeBadRequestWith(AuthMessages.MergeNoAccount);
    }

    /// <summary>
    /// Второй аккаунт удалили между просмотром и подтверждением, а состав пространств
    /// при этом не поменялся (у второго их нет) — всё равно 409 и новый просмотр, а не 500
    /// </summary>
    [Fact]
    public async Task OtherDeletedAfterPreviewConflicts()
    {
        var (_, boris) = new UserBuilder("Boris").Please();
        var (client, db) = await Client(x => x.Profiles.Add(boris));
        var preview = await Preview(client, PasswordProof("Boris"));
        await db.Users.Where(x => x.Id == boris.Id).ExecuteDeleteAsync();

        var response = await Merge(client, preview, _oleg.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await db.Users.AsNoTracking().AnyAsync(x => x.Id == _oleg.Id));
    }

    /// <summary>Аккаунт с ролью админки удалить нельзя (раздел 10): у обоих роли — отказ сразу</summary>
    [Fact]
    public async Task PreviewRefusesWhenBothAreAdmins()
    {
        var (client, _) = await Client(x => x.UserInAdminRoles.AddRange(
            new UserInAdminRole { UserId = _oleg.Id, Role = AdminRole.AdminManagement, GrantedUtc = DateTime.UtcNow },
            new UserInAdminRole { UserId = _second.Id, Role = AdminRole.AdminManagement, GrantedUtc = DateTime.UtcNow }));

        var response = await client.PostAsJsonAsync("/api/Account/MergePreview", TelegramProof(SecondTelegram));

        await response.ShouldBeBadRequestWith(AuthMessages.AdminCannotBeDeleted);
    }

    /// <summary>
    /// Роль админки у одного — просмотр это показывает (удалить его нельзя), а попытка
    /// удалить его всё равно отказ, и оба аккаунта целы
    /// </summary>
    [Fact]
    public async Task AdminCanOnlyStay()
    {
        var (client, db) = await Client(x => x.UserInAdminRoles.Add(
            new UserInAdminRole { UserId = _second.Id, Role = AdminRole.AdminManagement, GrantedUtc = DateTime.UtcNow }));
        var preview = await Preview(client, TelegramProof(SecondTelegram));
        Assert.False(preview.Other.CanBeRemoved);
        Assert.True(preview.Current.CanBeRemoved);

        var response = await Merge(client, preview, _oleg.Id);

        await response.ShouldBeBadRequestWith(AuthMessages.AdminCannotBeDeleted);
        Assert.True(await db.Users.AnyAsync(x => x.Id == _second.Id));
        Assert.True(await db.UserInPermissions.AnyAsync(x => x.UserId == _second.Id && x.WorkspaceId == _ponchiki.Id));
    }

    /// <summary>
    /// Подбор пароля второго аккаунта упирается в лимит входа (8.1) — по учётке второго:
    /// верный пароль сверх лимита тоже не открывает просмотр
    /// </summary>
    [Fact]
    public async Task PasswordProofIsRateLimited()
    {
        var (client, _) = await Client();
        for (var i = 0; i < AdminLoginLimiter.ProductAttemptsPerUser; i++)
        {
            await client.PostAsJsonAsync("/api/Account/MergePreview",
                new MergeProofDto { Login = "Petya", Password = $"guess-{i}" });
        }

        var response = await client.PostAsJsonAsync("/api/Account/MergePreview", PasswordProof("Petya"));

        await response.ShouldBeBadRequestWith(AuthMessages.TooManyAttempts);
    }

    /// <summary>
    /// Личная память ассистента удаляемого переезжает к остающемуся (спец. assistant, 12.5):
    /// факты «про меня» записаны со слов того же человека. Совпал ключ — остаётся факт
    /// остающегося, второго такого быть не может
    /// </summary>
    [Fact]
    public async Task PersonalFactsMoveToKept()
    {
        var moved = new AssistantFactBuilder(_roga.Id, "илья", "«Илья» — это Илья Петров").Of(_second.Id).Please();
        var clash = new AssistantFactBuilder(_roga.Id, "встречи", "встречи на два часа").Of(_second.Id).Please();
        var kept = new AssistantFactBuilder(_roga.Id, "встречи", "встречи обычно на час").Of(_oleg.Id).Please();

        var (_, db, _) = await MergeKeepingOleg(x => x.AssistantFacts.AddRange(moved, clash, kept));

        var facts = await db.AssistantFacts.AsNoTracking().OrderBy(x => x.Key).ToListAsync();
        Assert.Equal([kept.Id, moved.Id], facts.Select(x => x.Id));
        Assert.All(facts, x => Assert.Equal(_oleg.Id, x.UserId));
    }
}
