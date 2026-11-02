using System.ComponentModel.DataAnnotations;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Доказательство владения входом площадки (спец. accounts, 6 и 7.1): на сайте —
/// <c>id_token</c> окна «Log In with Telegram» или код VK ID с PKCE-verifier, в mini-app —
/// подписанные параметры запуска. Одно на привязку, развилку mini-app и
/// на просмотр слияния
/// </summary>
public class ExternalProofDto
{
    [Required]
    public ExternalProvider? Provider { get; set; }

    /// <summary>Вход на сайте: Telegram — <c>id_token</c> окна входа, VK — код</summary>
    public string? Code { get; set; }

    /// <summary>Только VK ID: PKCE-verifier к коду</summary>
    public string? Verifier { get; set; }

    /// <summary>Только VK ID</summary>
    public string? DeviceId { get; set; }

    /// <summary><c>state</c> входа: VK ждёт его при обмене кода, у Telegram это <c>nonce</c> токена</summary>
    public string? State { get; set; }

    /// <summary>Mini-app Telegram: подписанный <c>initData</c></summary>
    public string? InitData { get; set; }

    /// <summary>Mini-app VK: подписанные параметры запуска строкой — как <see cref="VkAuthDto.LaunchParams"/></summary>
    public string? VkLaunchParams { get; set; }
}

/// <summary>Тело <c>Account/Unlink</c> (раздел 6)</summary>
public class UnlinkDto
{
    [Required]
    public ExternalProvider? Provider { get; set; }
}
