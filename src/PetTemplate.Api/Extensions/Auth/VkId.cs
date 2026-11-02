using System.Text.Json;
using System.Text.Json.Serialization;
using PetTemplate.Shared.Options;
using Microsoft.Extensions.Options;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Обмен кода VK ID (OAuth 2.1 + PKCE) на пользователя VK (спец. accounts, 5.3). Один
/// на вход (<c>Auth/VkId</c>) и привязку (<c>Account/Link</c>).
///
/// Сервер сам меняет код на токен у <c>id.vk.ru</c> и берёт <c>user_id</c> из ответа:
/// он пришёл от VK напрямую, а не от клиента, — подделать его нечем. Секрета у обмена
/// нет: PKCE его заменяет (<c>code_verifier</c> знает только тот, кто начинал вход).
/// Токен доступа VK дальше не нужен и не хранится
/// </summary>
public class VkId(HttpClient http, IOptions<VkIdOptions> options, ILogger<VkId> logger)
{
    private VkIdOptions Options => options.Value;

    public bool IsEnabled => !string.IsNullOrEmpty(Options.AppId);

    /// <summary>
    /// Пользователь VK по коду. <paramref name="deviceId"/> и <paramref name="state"/> VK отдал
    /// вместе с кодом и ждёт их обратно. Обмен не удался — <c>null</c>
    /// </summary>
    public async Task<ExternalIdentity?> Exchange(string code, string verifier, string? deviceId, string? state,
        CancellationToken ct)
    {
        if (!IsEnabled || string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(Options.RedirectUri)) return null;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["client_id"] = Options.AppId,
            ["device_id"] = deviceId,
            ["redirect_uri"] = Options.RedirectUri
        };
        if (!string.IsNullOrEmpty(state)) form["state"] = state;

        TokenResponse? tokens;
        try
        {
            using var response = await http.PostAsync(Options.TokenEndpoint, new FormUrlEncodedContent(form), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("VK ID code exchange failed: {Status}", response.StatusCode);
                return null;
            }

            tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        }
        // VK не ответил или ответил не JSON — это отказ входа, а не ошибка сервера (5.4): человек
        // видит «Не удалось войти через VK», а не 500 после минуты ожидания. Отмена самим
        // запросом (ушёл со страницы) — не сбой площадки, её не глотаем
        catch (Exception e) when (e is HttpRequestException or JsonException
                                  || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "VK ID is unreachable");
            return null;
        }

        if (tokens?.UserId is not > 0)
        {
            logger.LogWarning("VK ID answered without user_id: {Error}", tokens?.Error);
            return null;
        }

        return new ExternalIdentity(tokens.UserId.Value, null);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("user_id")]
        public long? UserId { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
