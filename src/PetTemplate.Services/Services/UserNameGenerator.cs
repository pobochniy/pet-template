using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Validation;

namespace PetTemplate.Services.Services;

/// <summary>
/// Автоматический логин (спец. accounts, 4.2): регистрация через Telegram или VK
/// логин не спрашивает — это лишний шаг перед первым экраном, а поменять его
/// предлагает «Добро пожаловать».
///
/// <list type="number">
/// <item><c>@username</c> Telegram — если отвечает правилам 4.1 и свободен;</item>
/// <item>иначе <c>tg&lt;id&gt;</c> / <c>vk&lt;id&gt;</c>: id у площадки уникален, а шаблон
/// зарезервирован и человеку не достаётся.</item>
/// </list>
///
/// Занятым <c>tg&lt;id&gt;</c> всё же бывает: отвязка Telegram (VK) оставляет логин прежнему
/// аккаунту, и тот же вход, придя снова, заводит новый. Тогда — <c>tg&lt;id&gt;_2</c>,
/// <c>_3</c> и дальше (<see cref="Fallback"/>).
///
/// Логин сравнивается без учёта регистра — колонка <c>citext</c> (4.1), отдельного
/// нормализованного столбца, как в Lad и ASU, нет
/// </summary>
public class UserNameGenerator(ApplicationContext db)
{
    public async Task<string> ForTelegram(long telegramId, string? telegramUserName, CancellationToken ct)
    {
        var candidate = telegramUserName?.Trim().TrimStart('@');

        // Резерв проверяется и тут: `@tg42` в Telegram законен, а у нас это чужой
        // автоматический логин
        if (UserNameRule.IsAllowedForPerson(candidate) && !await IsTaken(candidate!, ct))
        {
            return candidate!;
        }

        return await Fallback(UserNameRule.ForTelegram(telegramId), ct);
    }

    /// <summary>
    /// Первый свободный из <paramref name="automatic"/>, <c>automatic_2</c>, <c>automatic_3</c>…
    /// Хвост — через <c>_</c>: так логин отвечает формату 4.1 и не попадает в резерв
    /// (резерв — только <c>tg</c>/<c>vk</c> и цифры). Занятые с этим началом читаются одним
    /// запросом, а не по одному на кандидата. <c>_</c> в шаблоне <c>LIKE</c> — любой символ,
    /// поэтому он экранируется
    /// </summary>
    public async Task<string> Fallback(string automatic, CancellationToken ct)
    {
        var prefix = automatic + "_";
        var pattern = prefix.Replace("\\", "\\\\").Replace("_", "\\_").Replace("%", "\\%") + "%";
        var taken = (await db.Profiles
                .Where(x => x.UserName == automatic || EF.Functions.ILike(x.UserName, pattern, "\\"))
                .Select(x => x.UserName)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(automatic)) return automatic;

        var suffix = 2;
        while (taken.Contains($"{prefix}{suffix}")) suffix++;
        return $"{automatic}_{suffix}";
    }

    public Task<bool> IsTaken(string userName, CancellationToken ct)
    {
        var trimmed = userName.Trim();
        return db.Profiles.AnyAsync(x => x.UserName == trimmed, ct);
    }
}
