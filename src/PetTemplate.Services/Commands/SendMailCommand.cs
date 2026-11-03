using PetTemplate.Services.Mail;

namespace PetTemplate.Services.Commands;

/// <summary>
/// Отправить письмо (спец. mail, 4). Ставится тем же <c>SaveChanges</c>,
/// что и токен (outbox): либо в базу уходят и токен, и задание, либо ничего.
///
/// Ссылки в команде нет: сам токен придумывает обработчик перед отправкой, а в базе
/// остаётся только хеш — иначе строка очереди хранила бы готовый вход в чужой аккаунт.
/// Ключ идемпотентности — <see cref="TokenId"/>: письмо по токену уходит один раз.
/// Адрес у писем со ссылкой — из токена, в команде его нет
/// </summary>
public class SendMailCommand : ICommand
{
    public static string QueueName => "send-mail";

    public MailKind Kind { get; set; }

    /// <summary>Токен аккаунта — у писем со ссылкой сброса или подтверждения</summary>
    public Guid? TokenId { get; set; }

    /// <summary>
    /// Адрес и язык — только у письма без токена («Почту сменили»): прежний адрес
    /// из профиля уже стёрт, взять его обработчику больше неоткуда
    /// </summary>
    public string? To { get; set; }

    public string? Language { get; set; }
}
