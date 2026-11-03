using System.Net;
using System.Reflection;
using System.Text.Json;
using PetTemplate.Shared.Constants;

namespace PetTemplate.Services.Mail;

/// <summary>
/// Шаблоны писем по языку профиля (спец. mail, 2 и 3). Тексты — в словарях сервера
/// (<c>Mail/Templates/{язык}.json</c>, ресурсы сборки), а не в коде. Подстановки —
/// <c>{link}</c> и <c>{hours}</c>, как в словарях клиента. Незнакомый язык — русский
/// </summary>
public static class MailTemplates
{
    private sealed record Template(string Subject, string Body);

    private static readonly Lazy<Dictionary<string, Dictionary<string, Template>>> Dictionaries = new(Load);

    public static MailMessage Render(MailKind kind, string? language, string to, string link, int hours = 0)
    {
        var lang = LanguageConstants.Normalize(language);
        var dictionary = Dictionaries.Value.GetValueOrDefault(lang) ?? Dictionaries.Value[LanguageConstants.Default];
        var template = dictionary[Key(kind)];

        var text = template.Body.Replace("{link}", link).Replace("{hours}", hours.ToString());
        var html = WebUtility.HtmlEncode(template.Body)
            .Replace("{link}", $"<a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(link)}</a>")
            .Replace("{hours}", hours.ToString())
            .Replace("\n", "<br>");

        return new MailMessage(to, template.Subject, text, $"<p>{html}</p>");
    }

    private static string Key(MailKind kind) => kind switch
    {
        MailKind.PasswordReset => "passwordReset",
        MailKind.EmailConfirm => "emailConfirm",
        MailKind.EmailChanged => "emailChanged",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static Dictionary<string, Dictionary<string, Template>> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        return LanguageConstants.Supported.ToDictionary(lang => lang, lang =>
        {
            using var stream = assembly.GetManifestResourceStream($"PetTemplate.Services.Mail.Templates.{lang}.json")
                               ?? throw new InvalidOperationException($"Mail templates for '{lang}' are missing");
            return JsonSerializer.Deserialize<Dictionary<string, Template>>(stream, options)!;
        });
    }
}
