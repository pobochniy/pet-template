using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Api.Extensions.Auth;

/// <summary>
/// Кто владеет входом площадки — по доказательству (спец. accounts, 6 и 7.1): подписанный
/// <c>id_token</c> окна «Log In with Telegram» или код VK ID на сайте, подписанные параметры
/// запуска в mini-app. Одно место на привязку, развилку mini-app и просмотр
/// слияния: разные копии проверяли бы по-разному
/// </summary>
public class ExternalProof(TelegramLogin telegram, VkId vk, MiniAppLaunch launch)
{
    /// <summary>Пользователь площадки; доказательство не прошло — <c>null</c></summary>
    public async Task<ExternalIdentity?> Resolve(ExternalProofDto proof, CancellationToken ct)
    {
        switch (proof.Provider)
        {
            case ExternalProvider.Telegram when !string.IsNullOrEmpty(proof.Code):
                return await telegram.Resolve(proof.Code, proof.State);

            case ExternalProvider.Telegram when !string.IsNullOrEmpty(proof.InitData):
                var user = launch.Telegram(proof.InitData);
                return user == null ? null : new ExternalIdentity(user.Id, user.Username, user.SignedAt);

            case ExternalProvider.Vk when !string.IsNullOrEmpty(proof.Code):
                return await vk.Exchange(proof.Code, proof.Verifier ?? string.Empty, proof.DeviceId, proof.State, ct);

            case ExternalProvider.Vk when !string.IsNullOrEmpty(proof.VkLaunchParams):
                var vkUser = launch.Vk(proof.VkLaunchParams);
                return vkUser == null ? null : new ExternalIdentity(vkUser.Id, null, vkUser.SignedAt, vkUser.Language);

            default:
                return null;
        }
    }
}
