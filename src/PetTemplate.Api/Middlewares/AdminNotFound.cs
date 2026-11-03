namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Админские пути (<c>/api/Admin*</c>, без учёта регистра — как маршруты) отказывают
/// одинаково: пустым <c>404</c>, как несуществующий путь (спец. admin, 3.5).
///
/// <see cref="AuthorizeAdmin"/> отвечает <c>404</c>, но до него доходят не все запросы:
/// чужой метод на известном пути routing отвечает <c>405</c> с заголовком <c>Allow</c>,
/// не-JSON тело — MVC отвечает <c>415</c>, а сам <c>404</c> фильтра MVC оборачивает в
/// ProblemDetails. По этой разнице в открытое окно видна карта админских методов. Поэтому
/// ответ админского пути копится в памяти (ответы админки маленькие) и <c>404</c>, <c>405</c>
/// и <c>415</c> уходят пустым <c>404</c>
/// </summary>
public class AdminNotFound(RequestDelegate next)
{
    private static readonly PathString AdminPath = new("/api/admin");

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(AdminPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = original;
        }

        if (context.Response.StatusCode is StatusCodes.Status404NotFound
            or StatusCodes.Status405MethodNotAllowed
            or StatusCodes.Status415UnsupportedMediaType)
        {
            context.Response.Headers.Clear();
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentLength = 0;
            return;
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, context.RequestAborted);
    }
}
