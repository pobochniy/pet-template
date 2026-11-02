namespace PetTemplate.Services.Mail;

/// <summary>Письмо, готовое к отправке: адрес, тема, текст и html</summary>
public record MailMessage(string To, string Subject, string Text, string Html);
