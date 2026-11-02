using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Services.Mail;

namespace PetTemplate.Tests.Mocks;

/// <summary>
/// Почта без SMTP: письма копятся здесь, а не уходят. Подключается вместо
/// <see cref="IMailSender"/> — весь путь от команды очереди до письма остаётся боевым
/// </summary>
public class FakeMailSender : IMailSender
{
    public ConcurrentQueue<MailMessage> Sent { get; } = new();

    /// <summary>Провайдер «упал»: отправка бросает, очередь должна повторить</summary>
    public bool IsDown { get; set; }

    public Task Send(MailMessage message, CancellationToken ct)
    {
        if (IsDown) throw new InvalidOperationException("Mock: SMTP is down");
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public Action<IServiceCollection> Services() => services => services.AddSingleton<IMailSender>(this);
}
