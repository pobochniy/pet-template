using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Admin;

namespace PetTemplate.Tests.Features.Account;

/// <summary>
/// Отзыв токенов (спец. accounts, 8.1, 10 и 11): токен, выпущенный до
/// <c>TokensValidAfterUtc</c>, и токен удалённого аккаунта — <c>401</c>; админский токен
/// отзывается тем же (спец. admin, 3.3) — <c>404</c>, как любой отказ админки
/// </summary>
public class SessionRevocationTests
{
    private const string AnyAuthorizedEndpoint = "/api/Account/Logins";

    [Fact]
    public async Task RejectsTokenIssuedBeforeRevocation()
    {
        var revokedAt = DateTime.UtcNow.AddMinutes(-5);
        var (_, oleg) = new UserBuilder("Oleg").Please();
        oleg.User!.TokensValidAfterUtc = revokedAt;
        var (client, _) = await TestSession.IssuedAt(oleg.Id, revokedAt.AddMinutes(-1), x => x.Profiles.Add(oleg));

        var response = await client.GetAsync(AnyAuthorizedEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AcceptsTokenIssuedAfterRevocation()
    {
        var revokedAt = DateTime.UtcNow.AddMinutes(-5);
        var (_, oleg) = new UserBuilder("Oleg").Please();
        oleg.User!.TokensValidAfterUtc = revokedAt;
        var (client, _) = await TestSession.IssuedAt(oleg.Id, revokedAt.AddMinutes(1), x => x.Profiles.Add(oleg));

        var response = await client.GetAsync(AnyAuthorizedEndpoint);

        await response.ShouldBeSuccessful();
    }

    /// <summary>
    /// Токен, выпущенный в ту же секунду, что отметка, действует: <c>iat</c> — целые
    /// секунды, и ответ на смену пароля иначе отдавал бы уже мёртвый токен
    /// </summary>
    [Fact]
    public async Task AcceptsTokenIssuedInTheSameSecond()
    {
        var revokedAt = DateTime.UtcNow.AddMinutes(-5);
        var (_, oleg) = new UserBuilder("Oleg").Please();
        SessionGuard.Revoke(oleg.User!, revokedAt);
        var (client, _) = await TestSession.IssuedAt(oleg.Id, revokedAt, x => x.Profiles.Add(oleg));

        var response = await client.GetAsync(AnyAuthorizedEndpoint);

        await response.ShouldBeSuccessful();
    }

    /// <summary>Аккаунта нет в базе (удалён, раздел 10) — токен не действует</summary>
    [Fact]
    public async Task RejectsTokenOfMissingUser()
    {
        var (client, _) = await TestSession.IssuedAt(Guid.CreateVersion7(), DateTime.UtcNow);

        var response = await client.GetAsync(AnyAuthorizedEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Токен из ответа входа несёт <c>iat</c> (<c>AuthResultFactory</c>): без него отзыв
    /// не отличил бы новый токен от старого
    /// </summary>
    [Fact]
    public async Task LoginTokenCarriesIssuedAt()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        var (client, _) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        var response = await client.PostAsJsonAsync("/api/Auth/LogIn",
            new LoginDto { Login = "Oleg", Password = UserBuilder.SuperAdminPassword });

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result!.Token);
        Assert.Contains(jwt.Claims, x => x.Type == JwtRegisteredClaimNames.Iat);
    }

    /// <summary>
    /// Админский токен гаснет вместе с токеном продукта (спец. admin, 3.3): в админку входят
    /// паролем продукта, и его смена после входа отзывает и эту сессию
    /// </summary>
    [Fact]
    public async Task RejectsAdminTokenIssuedBeforeRevocation()
    {
        var oleg = Given.SystemAdmin();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));
        var login = await client.PostAsJsonAsync("/api/Admin/Login",
            new LoginDto { Login = "Oleg", Password = UserBuilder.SuperAdminPassword });
        client.UseAdminSession((await login.Content.ReadFromJsonAsync<AdminLoginResultDto>())!);
        await RevokeLater(db, oleg.Id);

        var response = await client.GetAsync("/api/Admin/Me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Отметка отзыва после выпуска токена — как смена пароля минуту спустя. Кэш отметки
    /// (<see cref="SessionGuard"/>) живёт в хосте, а тест пишет в базу мимо него — но до этого
    /// запроса хост её ещё не читал
    /// </summary>
    private static async Task RevokeLater(ApplicationContext db, Guid userId)
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        SessionGuard.Revoke(user, DateTime.UtcNow.AddMinutes(1));
        await db.SaveChangesAsync();
    }
}
