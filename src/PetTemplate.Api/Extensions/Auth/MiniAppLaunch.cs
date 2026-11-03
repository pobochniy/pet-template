using System.Web;
using PetTemplate.Services.Interfaces;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Options;
using Microsoft.Extensions.Options;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Подписанные параметры запуска mini-app (спец. accounts, 5.6): подпись площадки
/// и её свежесть — одно место на вход, регистрацию после развилки, привязку и слияние.
/// Четыре копии разошлись бы: одна пускала бы по просроченной подписи.
///
/// Свежесть — <see cref="AccountOptions.LaunchParamsMaxAge"/>: площадка выдаёт новую
/// подпись на каждый запуск, а без срока подсмотренная однажды строка — вечный вход.
///
/// Слой API, а не сервисов: в Development подпись Telegram не проверяется вовсе —
/// mini-app без бота не запустить, — и это настройка хоста
/// </summary>
public class MiniAppLaunch(
    TelegramService telegram,
    IVkService vk,
    IOptions<AccountOptions> options,
    IWebHostEnvironment environment,
    ILogger<MiniAppLaunch> logger)
{
    /// <summary>
    /// Пользователь Telegram из <c>initData</c> и время подписи (<c>auth_date</c>); подпись
    /// не та или устарела — <c>null</c>
    /// </summary>
    public TelegramLaunch? Telegram(string? initData)
    {
        if (string.IsNullOrEmpty(initData)) return null;

        var authDate = HttpUtility.ParseQueryString(initData)["auth_date"];
        var hasDate = long.TryParse(authDate, out var seconds);

        if (!environment.IsDevelopment())
        {
            if (!telegram.ValidateInitData(initData))
            {
                logger.LogWarning("Telegram initData signature is invalid");
                return null;
            }

            if (!hasDate || !IsFresh(seconds))
            {
                logger.LogWarning("Telegram initData is stale: auth_date={AuthDate}", authDate);
                return null;
            }
        }

        var user = telegram.ExtractTelegramUser(initData);
        if (user == null) return null;

        // Без даты (бывает только в Development, где подпись не проверяется) — подпись
        // «давняя»: свежим такой вход не считается (8.3)
        return new TelegramLaunch(user.Id, user.Username, SignedAt(hasDate ? seconds : 0));
    }

    /// <summary>
    /// Пользователь mini-app Telegram и время подписи площадки — от него, а не от выдачи
    /// токена, считается свежесть входа (спец. accounts, 8.3)
    /// </summary>
    public record TelegramLaunch(long Id, string? Username, DateTimeOffset SignedAt);

    /// <summary>
    /// Mini-app открыт по ссылке-приглашению: <c>start_param</c> вида <c>inv_&lt;токен&gt;</c>
    /// (<c>t.me/&lt;бот&gt;?startapp=inv_…</c>). Параметр входит в подписанный <c>initData</c>,
    /// поэтому смотреть на него можно только после <see cref="Telegram"/>. С приглашением
    /// развилки нет — человек почти наверняка новый (спец. accounts, 5.6)
    /// </summary>
    public static bool HasInvitation(string initData) =>
        HttpUtility.ParseQueryString(initData)["start_param"]?.StartsWith("inv_", StringComparison.Ordinal) == true;

    /// <summary>
    /// Пользователь VK из параметров запуска; подпись не та или устарела — <c>null</c>.
    /// Подпись проверяется по строке как есть — разбор на поля и сборка обратно теряли бы
    /// параметры, о которых мы не знаем
    /// </summary>
    public VkUser? Vk(string? launchParams)
    {
        if (string.IsNullOrEmpty(launchParams)) return null;
        launchParams = launchParams.TrimStart('?');

        if (!vk.IsSignValid(launchParams))
        {
            logger.LogWarning("VK launch params signature is invalid");
            return null;
        }

        var query = HttpUtility.ParseQueryString(launchParams);
        if (!long.TryParse(query["vk_ts"], out var ts) || !IsFresh(ts))
        {
            logger.LogWarning("VK launch params are stale: vk_ts={Ts}", query["vk_ts"]);
            return null;
        }

        if (!long.TryParse(query["vk_user_id"], out var userId) || userId <= 0) return null;

        return new VkUser(userId, query["vk_language"], SignedAt(ts));
    }

    /// <summary>
    /// Пользователь mini-app VK, язык его интерфейса VK и время подписи
    /// (<c>vk_ts</c>) — от него считается свежесть входа (спец. accounts, 8.3)
    /// </summary>
    public record VkUser(long Id, string? Language, DateTimeOffset SignedAt);

    private static DateTimeOffset SignedAt(long unixSeconds) =>
        unixSeconds is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds) : DateTimeOffset.UnixEpoch;

    private bool IsFresh(long unixSeconds)
    {
        var signedAt = SignedAt(unixSeconds);
        var age = DateTimeOffset.UtcNow - signedAt;

        // Подпись «из будущего» больше минуты — не часы разъехались, а подделка времени
        return age <= options.Value.LaunchParamsMaxAge && age >= TimeSpan.FromMinutes(-1);
    }
}
