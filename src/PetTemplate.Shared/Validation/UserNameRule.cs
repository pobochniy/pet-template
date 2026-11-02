using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Validation;

/// <summary>
/// Правила логина (спец. accounts, 4.1) — одно место на регистрацию, регистрацию
/// по приглашению, смену в профиле и автоматический логин. Три копии разошлись бы
/// молча: профиль отбивал бы то, что пропускает регистрация.
///
/// <list type="bullet">
/// <item>3–20 символов: латинские буквы, цифры, <c>_</c> и <c>.</c>; первая — буква;</item>
/// <item>уникален без учёта регистра — это даёт тип колонки (<c>citext</c>, спец.
/// infrastructure, 3.1), а не отдельный нормализованный столбец, как в Lad и ASU;</item>
/// <item><c>tg&lt;цифры&gt;</c> и <c>vk&lt;цифры&gt;</c> зарезервированы под автоматические
/// логины (4.2): человек такой не выберет, и автоматический поэтому никогда не занят.</item>
/// </list>
///
/// Длину <c>citext</c> база не держит — её держит это правило на каждом входе, где логин
/// пишется: <c>RegisterDto</c>, <c>InvitationRegisterDto</c>, <c>ChangeUserNameDto</c>;
/// автоматический логин собирается здесь же и в предел укладывается сам.
///
/// Существующие логины, которые правилам не отвечают, остаются как есть: правила
/// действуют на ввод, а не на то, что уже лежит в базе
/// </summary>
public static partial class UserNameRule
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.]*$")]
    private static partial Regex Format();

    /// <summary>
    /// Без учёта регистра: <c>TG123</c> совпал бы с автоматическим <c>tg123</c> —
    /// уникальность логина регистра не различает
    /// </summary>
    [GeneratedRegex("^(tg|vk)[0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();

    /// <summary>Длина и набор символов — без резерва: автоматический логин им отвечает</summary>
    public static bool HasValidFormat(string? userName) =>
        userName is { Length: >= MinLength and <= MaxLength } && Format().IsMatch(userName);

    public static bool IsReserved(string userName) => Reserved().IsMatch(userName);

    /// <summary>Логин, который может выбрать человек: формат и не из резерва</summary>
    public static bool IsAllowedForPerson(string? userName) =>
        HasValidFormat(userName) && !IsReserved(userName!);

    /// <summary>Автоматический логин из Telegram-id (4.2)</summary>
    public static string ForTelegram(long telegramId) => $"tg{telegramId}";

    /// <summary>Автоматический логин из VK-id (4.2)</summary>
    public static string ForVk(long vkId) => $"vk{vkId}";
}

/// <summary>
/// Логин, введённый человеком, — по <see cref="UserNameRule"/>. Пустое значение
/// пропускает: обязательность — дело <c>[Required]</c>, как у остальных полей.
/// Наружу уезжает код, а не фраза (<c>src/PetTemplate.Api/agents.md</c>, «Валидация»)
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class UserNameAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string userName || string.IsNullOrEmpty(userName)) return ValidationResult.Success;

        var trimmed = userName.Trim();
        var memberNames = validationContext.MemberName is null ? null : new[] { validationContext.MemberName };

        if (!UserNameRule.HasValidFormat(trimmed))
        {
            return new ValidationResult(
                ValidationError.Pack(ValidationCodes.UserNameFormat,
                    ("min", UserNameRule.MinLength), ("max", UserNameRule.MaxLength)),
                memberNames);
        }

        return UserNameRule.IsReserved(trimmed)
            ? new ValidationResult(ValidationCodes.UserNameReserved, memberNames)
            : ValidationResult.Success;
    }
}
