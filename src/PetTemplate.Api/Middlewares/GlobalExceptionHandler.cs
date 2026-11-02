using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Статус ответа выбирается по типу исключения: «такого нет» — 404
/// (<see cref="NotFoundException"/>), «пока смотрели, всё поменялось» — 409
/// (<see cref="ConflictException"/>), «запрос неправильный» — 400, всё остальное — 500.
/// Отказ с кодом (<see cref="ICodedError"/>) уезжает с <c>code</c> и <c>params</c> рядом
/// с текстом — клиент переводит код по словарю (<c>server-error.util.ts</c>)
/// </summary>
public class GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger)
{
    /// <summary>
    /// Код и подстановки есть не у всякого отказа, и пустых полей в теле быть не должно:
    /// у клиента «нет кода» — это отсутствующее поле, а не <c>null</c>
    /// </summary>
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex)
        {
            logger.LogWarning(ex, "Bad request: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex.Message, HttpStatusCode.BadRequest);
        }
        catch (NotFoundException ex)
        {
            logger.LogWarning(ex, "Not found: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex.Message, HttpStatusCode.NotFound, ex);
        }
        catch (ConflictException ex)
        {
            logger.LogWarning(ex, "Conflict: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex.Message, HttpStatusCode.Conflict, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized access: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex.Message, HttpStatusCode.Unauthorized);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid operation: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex.Message, HttpStatusCode.BadRequest);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Argument exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex.Message, HttpStatusCode.BadRequest, ex as ICodedError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    private static async Task HandleExceptionAsync(
        HttpContext context,
        string message,
        HttpStatusCode statusCode,
        ICodedError? coded = null)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new ErrorResponse(message, (int)statusCode, coded?.Code, coded?.Params);

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, ResponseOptions));
    }

    /// <summary>
    /// Тело отказа. Поля с маленькой буквы — как их читает клиент; <c>code</c>
    /// и <c>params</c> уезжают только когда есть
    /// </summary>
    private sealed record ErrorResponse(
        string error,
        int statusCode,
        string? code,
        IReadOnlyDictionary<string, object>? @params);
}
