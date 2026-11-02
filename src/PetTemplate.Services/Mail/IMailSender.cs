namespace PetTemplate.Services.Mail;

/// <summary>
/// Отправитель писем (спец. mail, 2). Зовёт его только обработчик очереди
/// <c>SendMailCommandHandler</c>: письмо уходит не из запроса, а повтором очереди, если
/// провайдер упал. Тесты подменяют его заглушкой
/// </summary>
public interface IMailSender
{
    Task Send(MailMessage message, CancellationToken ct);
}
