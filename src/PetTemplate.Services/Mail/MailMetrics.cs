using System.Diagnostics.Metrics;

namespace PetTemplate.Services.Mail;

/// <summary>
/// Отправленные и неотправленные письма по виду (спец. mail, 2 и 7): молча
/// неработающее восстановление пароля иначе обнаружится только жалобой
/// </summary>
public class MailMetrics : IDisposable
{
    public const string MeterName = "PetTemplate.Mail";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _sent;
    private readonly Counter<long> _failed;

    public MailMetrics()
    {
        _sent = _meter.CreateCounter<long>("pettemplate.mail.sent");
        _failed = _meter.CreateCounter<long>("pettemplate.mail.failed");
    }

    public void Sent(MailKind kind) => _sent.Add(1, new KeyValuePair<string, object?>("kind", kind.ToString()));

    public void Failed(MailKind kind) => _failed.Add(1, new KeyValuePair<string, object?>("kind", kind.ToString()));

    public void Dispose() => _meter.Dispose();
}
