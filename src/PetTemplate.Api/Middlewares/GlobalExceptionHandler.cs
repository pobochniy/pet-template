using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using PetTemplate.Shared.Errors;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Статус ответа выбирается по типу исключения — только своих (<see cref="DomainException"/>):
/// «такого нет» — 404 (<see cref="NotFoundException"/>), «пока смотрели, всё поменялось» — 409
/// (<see cref="ConflictException"/>), «запрос неправильный» — 400 (<see cref="BadRequestException"/>,
/// у админки — <see cref="AdminRejectionException"/>), испорченный HTTP-запрос — 400
/// (<see cref="BadHttpRequestException"/>). Отказ с кодом (<see cref="ICodedError"/>) уезжает
/// с <c>code</c> и <c>params</c> рядом с текстом — клиент переводит код по словарю
/// (<c>server-error.util.ts</c>).
///
/// Всё остальное — баг, и <see cref="ArgumentException"/> с <see cref="InvalidOperationException"/>
/// тоже: их бросают библиотеки и <c>SingleAsync</c>, и текст их — подробности кода, а не ответ
/// человеку. Ответ — 500 с общей фразой и <c>traceId</c>, по которому запись найдётся в логе
/// </summary>
public class GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger)
{
    /// <summary>Текст ответа 500: подробности — в логе, по <c>traceId</c></summary>
    public const string InternalErrorText = "Internal server error";

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
        catch (DomainException ex)
        {
            logger.LogWarning(ex, "Rejected: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex.Message, StatusOf(ex), ex as ICodedError);
        }
        catch (Exception ex)
        {
            // Id трассы — тот же, что в Tempo (спец. infrastructure, 10); без трассы — id запроса
            var traceId = Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;
            logger.LogError(ex, "Unhandled exception, traceId {TraceId}", traceId);
            await HandleExceptionAsync(context, InternalErrorText, HttpStatusCode.InternalServerError, traceId: traceId);
        }
    }

    private static HttpStatusCode StatusOf(DomainException ex) => ex switch
    {
        NotFoundException => HttpStatusCode.NotFound,
        ConflictException => HttpStatusCode.Conflict,
        _ => HttpStatusCode.BadRequest
    };

    private static async Task HandleExceptionAsync(
        HttpContext context,
        string message,
        HttpStatusCode statusCode,
        ICodedError? coded = null,
        string? traceId = null)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new ErrorResponse(message, (int)statusCode, coded?.Code, coded?.Params, traceId);

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, ResponseOptions));
    }

    /// <summary>
    /// Тело отказа. Поля с маленькой буквы — как их читает клиент; <c>code</c>
    /// и <c>params</c> уезжают только когда есть, <c>traceId</c> — только у 500
    /// </summary>
    private sealed record ErrorResponse(
        string error,
        int statusCode,
        string? code,
        IReadOnlyDictionary<string, object>? @params,
        string? traceId);
}
