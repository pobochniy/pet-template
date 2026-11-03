namespace PetTemplate.Shared.Enums;

/// <summary>Что админ сделал с чужим аккаунтом (спец. accounts, 8.5 и 11). Значения только в конец</summary>
public enum AccountAdminActionKind
{
    /// <summary>Выдал ссылку сброса пароля</summary>
    IssueReset = 1,

    /// <summary>Удалил аккаунт без пространств</summary>
    Delete = 2,

    /// <summary>Освободил для аккаунта почту, вписанную в чужой</summary>
    ReleaseEmail = 3,

    /// <summary>
    /// У аккаунта сняли неподтверждённую почту — её освободили для другого. Вторая запись
    /// освобождения: без неё журнал аккаунта, у которого пропал адрес, молчал бы
    /// </summary>
    EmailReleased = 4,

    /// <summary>Освободил для аккаунта телефон, вписанный в чужой</summary>
    ReleasePhone = 5,

    /// <summary>У аккаунта сняли телефон — его освободили для другого. Вторая запись, как у почты</summary>
    PhoneReleased = 6
}
