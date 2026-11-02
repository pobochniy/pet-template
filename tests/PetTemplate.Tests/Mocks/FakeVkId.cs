using System.Net;
using System.Text;
using System.Text.Json;
using PetTemplate.Api.Extensions.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace PetTemplate.Tests.Mocks;

/// <summary>
/// <c>id.vk.ru</c> без сети: на обмен верного кода отвечает <c>user_id</c>, на остальное —
/// ошибкой, как настоящий. Подменяет первичный обработчик HTTP-клиента <see cref="VkId"/>
/// </summary>
public class FakeVkId
{
    public const string AppId = "5555555";
    public const string ValidCode = "good-code";

    public long VkUserId { get; set; } = 2308481;

    /// <summary>VK не отвечает — как сеть, которая обрывает соединение</summary>
    public bool Unreachable { get; set; }

    /// <summary>Что пришло на обмен последним — проверка, что сервер отдал device_id и verifier</summary>
    public IReadOnlyDictionary<string, string>? LastRequest { get; private set; }

    public IReadOnlyDictionary<string, string?> Settings() => new Dictionary<string, string?>
    {
        ["VkIdOptions:AppId"] = AppId,
        ["VkIdOptions:RedirectUri"] = "https://example.com/auth/vk"
    };

    public Action<IServiceCollection> Services() => services =>
        services.AddHttpClient<VkId>().ConfigurePrimaryHttpMessageHandler(() => new Handler(this));

    private sealed class Handler(FakeVkId owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (owner.Unreachable) throw new HttpRequestException("Connection refused");

            var form = await request.Content!.ReadAsStringAsync(ct);
            var values = form.Split('&').Select(x => x.Split('='))
                .ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
            owner.LastRequest = values;

            var valid = values.GetValueOrDefault("code") == ValidCode;
            var body = valid
                ? JsonSerializer.Serialize(new { access_token = "x", user_id = owner.VkUserId, token_type = "Bearer" })
                : JsonSerializer.Serialize(new { error = "invalid_grant" });

            return new HttpResponseMessage(valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
