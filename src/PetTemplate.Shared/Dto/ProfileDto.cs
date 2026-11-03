using PetTemplate.Shared.Constants;

namespace PetTemplate.Shared.Dto;

/// <summary>
/// Свой профиль — в ответе входа, <c>Profile/Details</c> и <c>Profile/Save</c>. Собирает
/// его только <c>ProfileMapping</c>: сущность наружу не едет.
///
/// На сохранении (<c>Profile/Save</c>) читаются только <see cref="IsAdult"/> и
/// <see cref="HasAcceptedTerms"/>: у логина свой вызов с отказом «занят»
/// (<c>Account/ChangeUserName</c>, спец. accounts, 4.3), контакты и комментарий —
/// блок «Основное» (<c>Account/Save</c>, раздел 6), аватар — <c>Profile/SetAvatar</c>
/// </summary>
public class ProfileDto
{
    public Guid Id { get; set; }

    /// <summary>Логин — только на чтение (4.3)</summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Логин подставлен автоматически (4.2): клиент предлагает его сменить плашкой
    /// </summary>
    public bool UserNameIsGenerated { get; set; }

    /// <summary>
    /// У аккаунта есть пароль (8.1): профиль предлагает «Задать пароль» или «Сменить
    /// пароль». Признак, а не хеш — сам пароль наружу не едет
    /// </summary>
    public bool HasPassword { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>Почта подтверждена письмом (8.4). Только наружу</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>
    /// Язык интерфейса и писем (8.4): после входа клиент переключается на него — выбор,
    /// сделанный на другом устройстве, доезжает сюда. Только наружу: меняют его
    /// <c>Account/SetLanguage</c> и <c>Account/Save</c>
    /// </summary>
    public string Language { get; set; } = LanguageConstants.Default;

    public int? AvatarId { get; set; }
    public string? Comment { get; set; }
    public bool IsAdult { get; set; }
    public bool HasAcceptedTerms { get; set; }

    /// <summary>
    /// Основное пространство — то, что открывается при входе (спец. authorization, 7.1).
    /// Во входе — посчитанное: указатель в профиле мог устареть, и <c>null</c> здесь
    /// значит «пространств нет». На сохранении профиля не читается: меняет его только
    /// <c>Workspace/SetPrimary</c>
    /// </summary>
    public Guid? PrimaryWorkspaceId { get; set; }
}
