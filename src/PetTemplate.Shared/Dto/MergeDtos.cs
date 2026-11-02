using System.ComponentModel.DataAnnotations;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Просмотр слияния (спец. accounts, 7.3 и 7.4): обе карточки и одноразовый ключ на
/// <c>AccountOptions.MergeTicketTtl</c>. Подтверждение (<c>Account/Merge</c>) доказательства
/// не просит повторно — оно проверено здесь
/// </summary>
public class MergePreviewDto
{
    public Guid Ticket { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Аккаунт, из которого начинали, — тот, что открыт сейчас</summary>
    public AccountCardDto Current { get; set; } = null!;

    /// <summary>Второй аккаунт — владение доказано паролем, Telegram или VK</summary>
    public AccountCardDto Other { get; set; } = null!;

    /// <summary>
    /// Можно ли оставить второй аккаунт, удалив текущий: второй заведён не меньше
    /// <c>AccountOptions.MergeKeepOtherMinAge</c> назад (7.3). Нельзя — остаётся только текущий
    /// </summary>
    public bool CanKeepOther { get; set; }
}

/// <summary>
/// Ответ <c>Account/Link</c>: вход привязан — новые «Основное»; вход принадлежит другому
/// аккаунту — привязки нет, вместо неё просмотр слияния (раздел 7) с ключом: код площадки
/// одноразовый, и второй раз доказать владение тем же кодом клиент не сможет
/// </summary>
public class LinkResultDto
{
    public AccountLoginsDto? Logins { get; set; }

    public MergePreviewDto? MergePreview { get; set; }
}

/// <summary>
/// «Объединить с другим аккаунтом» (спец. accounts, 7.1): владение вторым доказывается
/// его логином и паролем — или его Telegram/VK (<see cref="External"/>)
/// </summary>
public class MergeProofDto
{
    public string? Login { get; set; }

    public string? Password { get; set; }

    public ExternalProofDto? External { get; set; }
}

/// <summary>Подтверждение слияния (7.4): ключ просмотра и какой аккаунт остаётся</summary>
public class MergeDto
{
    public Guid Ticket { get; set; }

    public Guid KeepUserId { get; set; }
}

/// <summary>Удаление аккаунта (раздел 9): подтверждение — свой логин</summary>
public class DeleteAccountDto
{
    [Required]
    public string UserName { get; set; } = null!;
}
