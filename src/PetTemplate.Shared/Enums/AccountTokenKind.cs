namespace PetTemplate.Shared.Enums;

/// <summary>Вид одноразового токена аккаунта (спец. accounts, 11)</summary>
public enum AccountTokenKind
{
    /// <summary>Ссылка сброса пароля — из письма (8.4) или от поддержки (8.5)</summary>
    PasswordReset = 1,

    /// <summary>Ссылка подтверждения почты (8.4)</summary>
    EmailConfirm = 2
}
