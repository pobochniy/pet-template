using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Services;

/// <summary>
/// Хеш пароля — одно место на регистрацию, вход, «задать», «сменить» и сброс
/// (спец. accounts, 8.1). Копия схемы в каждом методе разъехалась бы молча:
/// пароль, заданный одной копией, не подходил бы ко входу другой.
///
/// Схема — PBKDF2-SHA512: хеш нарочно медленный, чтобы утёкшую базу нельзя было
/// перебрать словарём на видеокарте. Строка хеша несёт схему, число итераций и соль:
/// <c>pbkdf2-sha512$210000$&lt;соль&gt;$&lt;хеш&gt;</c> — итерации можно поднять, не
/// ломая старые хеши. Прежняя схема шаблона v1 — один SHA-256 от «соль#пароль», соль в
/// <c>SecurityStamp</c> — ещё проверяется: такие хеши переезжают на новую схему при
/// первом успешном входе (<see cref="NeedsRehash"/>), миграции не нужно. Прода у шаблона
/// нет, но проекты, уже порождённые из v1, так переезжают без сброса паролей
/// </summary>
public static class PasswordHasher
{
    private const string Scheme = "pbkdf2-sha512";

    /// <summary>Итераций — по рекомендации OWASP для PBKDF2-HMAC-SHA512</summary>
    private const int Iterations = 210_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Записать пароль аккаунту: новая соль и хеш по текущей схеме</summary>
    public static void Set(User user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, Iterations);
        user.PasswordHash = $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        // Соль прежней схемы больше не нужна — она теперь внутри хеша
        user.SecurityStamp = null;
    }

    /// <summary>Подходит ли пароль. Аккаунт без пароля не подходит ни к какому</summary>
    public static bool Verify(User user, string password)
    {
        if (user.PasswordHash == null) return false;

        if (Parse(user.PasswordHash) is { } stored)
        {
            return CryptographicOperations.FixedTimeEquals(
                stored.Hash, Derive(password, stored.Salt, stored.Iterations));
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(user.PasswordHash),
            Encoding.ASCII.GetBytes(LegacyHash(user.SecurityStamp, password)));
    }

    /// <summary>
    /// Хеш записан прежней схемой или с меньшим числом итераций — после успешной
    /// проверки его стоит пересчитать: пароль в руках только в этот момент
    /// </summary>
    public static bool NeedsRehash(User user) =>
        user.PasswordHash != null && (Parse(user.PasswordHash) is not { } stored || stored.Iterations < Iterations);

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, HashBytes);

    /// <summary>Разобрать строку текущей схемы; прежняя или битая — <c>null</c></summary>
    private static (int Iterations, byte[] Salt, byte[] Hash)? Parse(string value)
    {
        var parts = value.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme) return null;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
            return null;

        try
        {
            return (iterations, Convert.FromBase64String(parts[2]), Convert.FromBase64String(parts[3]));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Прежняя схема шаблона v1 (<c>AuthService.GetHashString</c>): SHA-256 от «соль#пароль»
    /// заглавными шестнадцатеричными
    /// </summary>
    private static string LegacyHash(string? salt, string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}#{password}")));
}
