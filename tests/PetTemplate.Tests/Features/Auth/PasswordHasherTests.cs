using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Auth;

/// <summary>
/// Хеш пароля — PBKDF2-SHA512 (спец. accounts, 8.1); хеши прежней схемы шаблона v1 (SHA-256
/// от «соль#пароль») ещё подходят ко входу и переезжают на новую схему при первом
/// успешном входе
/// </summary>
public class PasswordHasherTests
{
    private const string Password = "Parol-Olega-42";

    [Fact]
    public void NewHashVerifiesOnlyItsPassword()
    {
        var user = new User();

        PasswordHasher.Set(user, Password);

        Assert.StartsWith("pbkdf2-sha512$", user.PasswordHash);
        Assert.True(PasswordHasher.Verify(user, Password));
        Assert.False(PasswordHasher.Verify(user, Password + "!"));
        Assert.False(PasswordHasher.NeedsRehash(user));
    }

    [Fact]
    public void SamePasswordGetsDifferentSalt()
    {
        var first = new User();
        var second = new User();

        PasswordHasher.Set(first, Password);
        PasswordHasher.Set(second, Password);

        Assert.NotEqual(first.PasswordHash, second.PasswordHash);
    }

    /// <summary>Хеш прежней схемы подходит, но просится на пересчёт</summary>
    [Fact]
    public void LegacyHashVerifiesAndNeedsRehash()
    {
        var (_, profile) = new UserBuilder("Oleg").Please();

        Assert.True(PasswordHasher.Verify(profile.User!, UserBuilder.SuperAdminPassword));
        Assert.False(PasswordHasher.Verify(profile.User!, Password));
        Assert.True(PasswordHasher.NeedsRehash(profile.User!));
    }

    /// <summary>Меньше итераций, чем сейчас, — тоже на пересчёт: число можно поднимать</summary>
    [Fact]
    public void FewerIterationsNeedRehash()
    {
        var user = new User();
        PasswordHasher.Set(user, Password);
        user.PasswordHash = user.PasswordHash!.Replace("$210000$", "$1000$");

        Assert.False(PasswordHasher.Verify(user, Password));
        Assert.True(PasswordHasher.NeedsRehash(user));
    }

    [Fact]
    public void NoPasswordMatchesNothing()
    {
        Assert.False(PasswordHasher.Verify(new User(), string.Empty));
        Assert.False(PasswordHasher.NeedsRehash(new User()));
    }

    /// <summary>Вход с хешем прежней схемы переписывает его новой — и следующий вход проходит</summary>
    [Fact]
    public async Task LoginMovesLegacyHashToNewScheme()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));
        var login = new LoginDto { Login = "Oleg", Password = UserBuilder.SuperAdminPassword };

        await (await client.PostAsJsonAsync("/api/Auth/LogIn", login)).ShouldBeSuccessful();

        var stored = await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.StartsWith("pbkdf2-sha512$", stored.PasswordHash);
        Assert.Null(stored.SecurityStamp);
        await (await client.PostAsJsonAsync("/api/Auth/LogIn", login)).ShouldBeSuccessful();
    }

    /// <summary>Вход отмечает время — по нему поддержка проверяет владельца (8.5)</summary>
    [Fact]
    public async Task LoginStampsLastLoginTime()
    {
        var (_, oleg) = new UserBuilder("Oleg").Please();
        var (client, db) = await Given.ApiClient(x => x.Profiles.Add(oleg));

        await client.PostAsJsonAsync("/api/Auth/LogIn",
            new LoginDto { Login = "Oleg", Password = UserBuilder.SuperAdminPassword });

        var stored = await db.Users.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
        Assert.NotNull(stored.LastLoginAtUtc);
    }
}
