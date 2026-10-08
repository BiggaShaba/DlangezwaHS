using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Emails are queued and sent in the background so saving a record never waits
// on the SMTP server (each send used to connect, log in and disconnect inline).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class EmailQueue
{
    private readonly Channel<MimeMessage> _channel = Channel.CreateUnbounded<MimeMessage>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(MimeMessage message) => _channel.Writer.TryWrite(message);

    public ChannelReader<MimeMessage> Reader => _channel.Reader;
}

public sealed class EmailSenderJob : BackgroundService
{
    private readonly EmailQueue _queue;
    private readonly SmtpOptions _opts;
    private readonly ILogger<EmailSenderJob> _logger;

    public EmailSenderJob(EmailQueue queue, SmtpOptions opts, ILogger<EmailSenderJob> logger)
    {
        _queue = queue;
        _opts = opts;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            // One connection for everything currently waiting (e.g. all kitchen staff + housemasters)
            using var smtp = new SmtpClient();
            try
            {
                var secure = _opts.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
                await smtp.ConnectAsync(_opts.Host, _opts.Port, secure, stoppingToken);
                if (!string.IsNullOrEmpty(_opts.UserName))
                    await smtp.AuthenticateAsync(_opts.UserName, _opts.Password, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not connect to the SMTP server; dropping queued emails");
                while (_queue.Reader.TryRead(out var dropped))
                    _logger.LogError("Email not sent to {Email} – Subject: {Subject}", dropped.To, dropped.Subject);
                continue;
            }

            while (_queue.Reader.TryRead(out var message))
            {
                try
                {
                    await smtp.SendAsync(message, stoppingToken);
                    _logger.LogInformation("Email sent to {Email} – Subject: {Subject}", message.To, message.Subject);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to send email to {Email}", message.To);
                    if (!smtp.IsConnected) break;
                }
            }

            if (smtp.IsConnected) await smtp.DisconnectAsync(true, stoppingToken);
        }
    }
}
