using PetTemplate.Shared.Enums;

namespace PetTemplate.Shared.Dto.Admin;

/// <summary>Строка поиска аккаунтов в админке (спец. accounts, 8.5)</summary>
public class AdminAccountDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool HasTelegram { get; set; }
    public bool HasVk { get; set; }
    public DateTime RegisteredAtUtc { get; set; }
}

/// <summary>
/// Карточка аккаунта в админке (8.5) — то, по чему проверяют владельца, не раскрывая
/// содержимого: логин, регистрация, способы войти, пространства с участниками,
/// последний вход (<see cref="AccountCardDto"/> — та же, что у слияния и удаления).
/// Содержимого пространств нет
/// </summary>
public class AdminAccountCardDto
{
    public AccountCardDto Card { get; set; } = null!;
    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>У аккаунта роль в админке — ни ссылки, ни удаления: сначала роли снимает другой админ</summary>
    public bool IsAdmin { get; set; }

    /// <summary>Что делали с аккаунтом из админки — свежие сверху</summary>
    public List<AdminAccountActionDto> Actions { get; set; } = [];
}

public class AdminAccountActionDto
{
    public AccountAdminActionKind Kind { get; set; }
    public string? AdminName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// Признаки журнала аккаунтов (спец. accounts, 8.5): логин (часть, без учёта регистра)
/// или id аккаунта целиком; курсор — последняя запись прошлой страницы
/// </summary>
public class AdminAccountJournalFilterDto
{
    public string? Query { get; set; }
    public Guid? Before { get; set; }
}

public class AdminAccountJournalPageDto
{
    public AdminAccountJournalEntryDto[] Entries { get; set; } = [];

    public Guid? NextBefore { get; set; }
}

/// <summary>
/// Строка журнала: действие админа с чужим аккаунтом (<see cref="Kind"/> задан) или
/// слияние (<see cref="Kind"/> пуст — тогда <see cref="TargetUserId"/> — удалённый,
/// <see cref="KeptUserId"/> — оставшийся). Логин цели — копия из журнала: аккаунта
/// может уже не быть; <see cref="TargetExists"/> — есть ли куда вести ссылкой
/// </summary>
public class AdminAccountJournalEntryDto
{
    public Guid Id { get; set; }
    public DateTime CreatedUtc { get; set; }

    /// <summary>Пусто — слияние</summary>
    public AccountAdminActionKind? Kind { get; set; }

    public Guid TargetUserId { get; set; }
    public string TargetUserName { get; set; } = string.Empty;
    public bool TargetExists { get; set; }

    /// <summary>Админ; удалён — пусто, и имени тоже нет</summary>
    public Guid? AdminUserId { get; set; }
    public string? AdminName { get; set; }

    public string? Reason { get; set; }

    /// <summary>Слияние: оставшийся аккаунт; удалят и его — пусто</summary>
    public Guid? KeptUserId { get; set; }
    public string? KeptUserName { get; set; }

    /// <summary>Слияние: чем доказано владение вторым — <c>password</c>, <c>telegram</c>, <c>vk</c></summary>
    public string? Platform { get; set; }
}

/// <summary>Работа с чужим аккаунтом — причина обязательна (8.5): ссылка сброса, удаление</summary>
public class AdminAccountReasonDto
{
    public string? Reason { get; set; }
}

/// <summary>Ссылка сброса — админ отправляет её человеку сам, в тот же разговор поддержки</summary>
public class AdminIssuedResetDto
{
    public string Url { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Прежняя ссылка ещё жила — эта выдана взамен, с её сроком</summary>
    public bool Replaced { get; set; }
}

/// <summary>
/// Освободить почту (8.4, 8.5): адрес, вписанный в чужой аккаунт без подтверждения, —
/// тот, кто просит, и есть аккаунт карточки
/// </summary>
public class AdminReleaseEmailDto
{
    public string? Email { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Освободить телефон (спец. accounts, 6): номер, вписанный в чужой
/// аккаунт, — подтверждения у телефона нет, поэтому снимается с любого
/// </summary>
public class AdminReleasePhoneDto
{
    public string? Phone { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Почта или телефон сняты: с какого аккаунта</summary>
public class AdminReleasedContactDto
{
    /// <summary>Логин аккаунта, с которого сняли адрес или номер</summary>
    public string ReleasedFromUserName { get; set; } = string.Empty;
}
