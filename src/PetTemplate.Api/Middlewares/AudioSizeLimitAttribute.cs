using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Потолок тела запроса с записью голоса — из конфигурации (<c>Transcription.MaxAudioBytes</c>,
/// спец. assistant, 6.1), а не константой в <c>[RequestSizeLimit]</c>: потолок один на
/// проверку в сервисе и на чтение тела, и разъехаться им незачем.
///
/// Фильтр ресурса, а не проверка в методе: он срабатывает до привязки модели, то есть
/// до того, как Kestrel начнёт читать тело, — запись в тридцать мегабайт отбрасывается
/// на чтении, а не после него. Сверху добавлен запас на обёртку multipart
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AudioSizeLimitAttribute : Attribute, IResourceFilter
{
    /// <summary>Заголовки частей и поле длительности — с запасом</summary>
    private const long MultipartOverhead = 64 * 1024;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is not { IsReadOnly: false }) return;

        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<AssistantOptions>>();
        feature.MaxRequestBodySize = options.Value.Transcription.MaxAudioBytes + MultipartOverhead;
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
