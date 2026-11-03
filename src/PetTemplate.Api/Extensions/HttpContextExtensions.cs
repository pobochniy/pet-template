using PetTemplate.Shared.Constants;

namespace PetTemplate.Api.Extensions;

public static class HttpContextExtensions
{
    /// <summary>Заголовок часового пояса клиента (спец. assistant, раздел 10)</summary>
    public const string TimeZoneHeader = "X-Time-Zone";

    /// <summary>
    /// Активное пространство из заголовка <c>X-Workspace-Id</c> (спец. authorization, 1.2).
    /// Нет заголовка или в нём не <c>Guid</c> — <c>400</c>: без пространства запросу
    /// внутри него делать нечего, а угадывать его по правам пользователя значило бы
    /// молча выбрать за человека
    /// </summary>
    public static Guid GetWorkspaceId(this HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(WorkspaceConstants.Header, out var value))
        {
            throw new BadHttpRequestException($"Header '{WorkspaceConstants.Header}' is missing.", 400);
        }

        if (!Guid.TryParse(value.ToString(), out var workspaceId))
        {
            throw new BadHttpRequestException($"Header '{WorkspaceConstants.Header}' is not a valid Guid.", 400);
        }

        return workspaceId;
    }

    /// <summary>
    /// Часовой пояс человека из заголовка <c>X-Time-Zone</c> строкой IANA (спец. assistant,
    /// раздел 10): от него ассистент считает «завтра». Нет заголовка или зона неизвестна —
    /// UTC и запись в логах, а не отказ запроса: ошибка в зоне не должна стоить человеку ответа
    /// </summary>
    public static string GetTimeZone(this HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(TimeZoneHeader, out var value)) return "UTC";

        var zone = value.ToString();
        if (string.IsNullOrWhiteSpace(zone)) return "UTC";

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
        {
            context.RequestServices
                .GetService<ILoggerFactory>()?
                .CreateLogger("Assistant.TimeZone")
                .LogWarning("Unknown time zone '{TimeZone}' in X-Time-Zone, using UTC", zone);

            return "UTC";
        }

        return zone;
    }
}
