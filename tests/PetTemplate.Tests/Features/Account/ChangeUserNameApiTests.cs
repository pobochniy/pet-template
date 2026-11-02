using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Errors;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Account;

/// <summary>Смена логина — <c>Account/ChangeUserName</c> (спец. accounts, 4.3)</summary>
public class ChangeUserNameApiTests
{
    private const string Url = "/api/Account/ChangeUserName";

    [Fact]
    public async Task CanChangeUserName()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync(Url, new ChangeUserNameDto { UserName = "Olezhka" });

        await response.ShouldBeSuccessful();
        var saved = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.Equal("Olezhka", saved.UserName);
    }

    /// <summary>Ответ — сессия с новым логином: клиент кладёт её к себе</summary>
    [Fact]
    public async Task AnswersWithSessionCarryingNewUserName()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync(Url, new ChangeUserNameDto { UserName = "Olezhka" });

        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        Assert.Equal("Olezhka", session!.Profile.UserName);
        Assert.False(string.IsNullOrWhiteSpace(session.Token));
    }

    /// <summary>Первая смена снимает плашку автоматического логина (4.2)</summary>
    [Fact]
    public async Task ChangeClearsGeneratedFlag()
    {
        var (token, oleg) = new UserBuilder("tg12345").Please();
        oleg.UserNameIsGenerated = true;
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync(Url, new ChangeUserNameDto { UserName = "Oleg" });

        await response.ShouldBeSuccessful();
        Assert.False((await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).UserNameIsGenerated);
    }

    /// <summary>Занятый — отказ, а не подмена; занятость — без учёта регистра (4.1, `citext`)</summary>
    [Fact]
    public async Task RejectsTakenInOtherCase()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (_, vasya) = new UserBuilder("Vasya").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.AddRange(oleg, vasya), token);

        var response = await client.PostAsJsonAsync(Url, new ChangeUserNameDto { UserName = "VASYA" });

        await response.ShouldBeBadRequestWith(AuthMessages.UserNameTaken);
        Assert.Equal("Oleg", (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).UserName);
    }

    /// <summary>Свой же логин в другом регистре — законная смена, а не «занят»</summary>
    [Fact]
    public async Task CanChangeCaseOfOwnUserName()
    {
        var (token, oleg) = new UserBuilder("oleg").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync(Url, new ChangeUserNameDto { UserName = "Oleg" });

        await response.ShouldBeSuccessful();
        Assert.Equal("Oleg", (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).UserName);
    }

    /// <summary>
    /// Правила логина держат и длину: у <c>citext</c> в базе предела нет
    /// (спец. infrastructure, 3.1), и 21 символ лёг бы как есть
    /// </summary>
    [Theory]
    [InlineData("ol", ValidationCodes.UserNameFormat)]
    [InlineData("Olezhka_Petrovich_123", ValidationCodes.UserNameFormat)]
    [InlineData("Олег", ValidationCodes.UserNameFormat)]
    [InlineData("1oleg", ValidationCodes.UserNameFormat)]
    [InlineData("tg42", ValidationCodes.UserNameReserved)]
    [InlineData("VK42", ValidationCodes.UserNameReserved)]
    public async Task RejectsUserNameAgainstRules(string userName, string code)
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync(Url, new ChangeUserNameDto { UserName = userName });

        await response.ShouldBeValidationWith("userName", code);
    }

    [Fact]
    public async Task RequiresSession()
    {
        var (client, _) = await Given.ApiClient();

        var response = await client.PostAsJsonAsync(Url, new ChangeUserNameDto { UserName = "Oleg" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
