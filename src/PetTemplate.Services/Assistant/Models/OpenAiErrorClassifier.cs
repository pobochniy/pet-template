using System.ClientModel;
using System.Net;
using System.Net.Sockets;

namespace PetTemplate.Services.Assistant.Models;

/// <summary>
/// Разбор отказов OpenAI-совместимого транспорта (спец. assistant, 16.2): клиент OpenAI
/// бросает <see cref="ClientResultException"/> со статусом ответа (0 — ответа нет вовсе),
/// сеть под ним — <see cref="HttpRequestException"/>, <see cref="SocketException"/>,
/// <see cref="IOException"/>. Причина ищется по цепочке <c>InnerException</c>
/// </summary>
public sealed class OpenAiErrorClassifier : IProviderErrorClassifier
{
    public static readonly OpenAiErrorClassifier Instance = new();

    /// <summary>
    /// Плохой ответ модели — пустой, не по делу, без вызова инструмента — сюда
    /// не попадает вовсе: он не бросает. 408 и 429 — отказ: поставщик жив, но ответа нет,
    /// и запасная как раз выручит
    /// </summary>
    public bool IsFailure(Exception error, CancellationToken ct)
    {
        for (var e = error; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case OperationCanceledException:
                    return !ct.IsCancellationRequested;
                case ClientResultException { Status: 0 }:
                    // Ответа нет вовсе — сеть; настоящая причина обычно внутри
                    return true;
                case ClientResultException result:
                    return Failing((HttpStatusCode)result.Status);
                case HttpRequestException { StatusCode: { } status }:
                    return Failing(status);
                case HttpRequestException or SocketException or IOException or TimeoutException:
                    return true;
            }
        }

        return false;
    }

    public bool Answered(Exception error)
    {
        for (var e = error; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case ClientResultException { Status: > 0 }:
                case HttpRequestException { StatusCode: not null }:
                    return true;
            }
        }

        return false;
    }

    private static bool Failing(HttpStatusCode status) =>
        (int)status >= 500
        || status is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests;
}
