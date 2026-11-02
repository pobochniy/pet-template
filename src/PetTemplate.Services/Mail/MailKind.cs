namespace PetTemplate.Services.Mail;

/// <summary>Виды писем (спец. mail, 3). Метка <c>kind</c> у метрик — имя значения</summary>
public enum MailKind
{
    None = 0,

    /// <summary>Ссылка сброса пароля — <c>/auth/reset/&lt;токен&gt;</c></summary>
    PasswordReset = 1,

    /// <summary>Ссылка подтверждения почты — <c>/auth/confirm-email/&lt;токен&gt;</c></summary>
    EmailConfirm = 2,

    /// <summary>«Почту в аккаунте сменили» — прежнему подтверждённому адресу</summary>
    EmailChanged = 3
}
