using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Errors;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Account;

/// <summary>Язык профиля — <c>Account/SetLanguage</c>, <c>Account/Save</c>, ответ входа (спец. accounts, 8.4)</summary>
public class LanguageApiTests
{
    [Fact]
    public async Task CanSetLanguage()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/SetLanguage", new SetLanguageDto { Language = "EN" });

        await response.ShouldBeSuccessful();
        Assert.Equal("en", (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).Language);
    }

    /// <summary>Язык выбирают из списка — незнакомый код ошибка клиента, а не «по умолчанию»</summary>
    [Fact]
    public async Task RejectsUnknownLanguage()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/SetLanguage", new SetLanguageDto { Language = "de" });

        await response.ShouldBeValidationWith("language", ValidationCodes.Language);
    }

    /// <summary>Форма «Основное» о языке не знает: без языка <c>Save</c> его не трогает</summary>
    [Fact]
    public async Task SaveWithoutLanguageKeepsIt()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        oleg.Language = "en";
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Comment = "Привет" });

        await response.ShouldBeSuccessful();
        Assert.Equal("en", (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).Language);
    }

    [Fact]
    public async Task SaveCanChangeLanguage()
    {
        var (token, oleg) = new UserBuilder("Oleg").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg), token);

        var response = await client.PostAsJsonAsync("/api/Account/Save", new AccountSaveDto { Language = "en" });

        await response.ShouldBeSuccessful();
        Assert.Equal("en", (await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id)).Language);
    }

    /// <summary>Вход отдаёт язык профиля: выбор с другого устройства доезжает до этого</summary>
    [Fact]
    public async Task LoginReturnsLanguage()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        oleg.Language = "en";
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn",
            new LoginDto { Login = "Oleg", Password = UserBuilder.SuperAdminPassword });

        await response.ShouldBeSuccessful();
        Assert.Equal("en", (await response.Content.ReadFromJsonAsync<AuthenticationResultDto>())!.Profile.Language);
    }
}
