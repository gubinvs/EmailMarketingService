using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace EmailMarketingService;

public class BatchEmailSender : BackgroundService
{
    private readonly ILogger<BatchEmailSender> _log;
    private readonly IEmailQueue _queue;
    private readonly IPendingStore _store;
    private readonly IServiceProvider _sp;
    private readonly IHttpClientFactory _http;
    private readonly SmtpOptions _smtp;

    private const int BatchSize = 400;
    private readonly TimeSpan DelayBetweenBatches = TimeSpan.FromHours(24);
    private readonly TimeSpan DelayBetweenEmails = TimeSpan.FromSeconds(2);
    private readonly string _notifyUponFinish = "gubinvs@gmail.com";

    public BatchEmailSender(
        ILogger<BatchEmailSender> log,
        IEmailQueue queue,
        IPendingStore store,
        IServiceProvider sp,
        IHttpClientFactory http,
        IOptions<SmtpOptions> smtp)
    {
        _log = log;
        _queue = queue;
        _store = store;
        _sp = sp;
        _http = http;
        _smtp = smtp.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("BatchEmailSender started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var state = await _store.LoadAsync(stoppingToken);
                    var pendingEmails = state.Pending.Where(p => !p.Sent).ToList();

                    if (pendingEmails.Any())
                    {
                        _log.LogInformation("Pending emails to send: {count}", pendingEmails.Count);

                        foreach (var batch in pendingEmails.Chunk(BatchSize))
                        {
                            var body = await LoadEmailBody();

                            foreach (var item in batch)
                            {
                                try
                                {
                                    await SendEmail(item.Email, body);
                                    item.Sent = true;

                                    // сохраняем сразу после успешной отправки
                                    await _store.SaveAsync(state);
                                }
                                catch (Exception ex)
                                {
                                    _log.LogWarning(ex, "Failed to send email to {email}, will retry later", item.Email);
                                }

                                await Task.Delay(DelayBetweenEmails, stoppingToken);
                            }
                        }

                        state.NextRunUtc = DateTime.UtcNow.Add(DelayBetweenBatches);
                        await _store.SaveAsync(state);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_notifyUponFinish))
                        {
                            _log.LogInformation("All emails sent. Sending notification to {email}", _notifyUponFinish);
                            await SendNotification(_notifyUponFinish);
                        }
                    }
                }
                catch (Exception ex) when (ex is not TaskCanceledException)
                {
                    _log.LogError(ex, "Error occurred in BatchEmailSender");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (TaskCanceledException)
        {
            _log.LogInformation("BatchEmailSender stopped by cancellation token");
        }
    }

    private async Task<string> LoadEmailBody()
    {
        return await Task.FromResult("<html><body>Hello!</body></html>");
    }

    private async Task SendEmail(string email, string body)
    {
        // Проверяем FromEmail
        if (string.IsNullOrWhiteSpace(_smtp.FromEmail))
            throw new InvalidOperationException("SMTP FromEmail address is not configured.");

        if (string.IsNullOrWhiteSpace(email))
        {
            _log.LogWarning("Skipping empty recipient email");
            return;
        }

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            Credentials = new System.Net.NetworkCredential(_smtp.User, _smtp.Password),
            EnableSsl = _smtp.UseSsl
        };

        var fromAddress = new MailAddress(_smtp.FromEmail, _smtp.FromName);
        var message = new MailMessage
        {
            From = fromAddress,
            Subject = "Рассылка",
            Body = body,
            IsBodyHtml = true
        };
        message.To.Add(email);

        await client.SendMailAsync(message);
        _log.LogInformation("Email successfully sent to {email}", email);
    }

    private async Task SendNotification(string email)
    {
        if (string.IsNullOrWhiteSpace(_smtp.FromEmail))
        {
            _log.LogWarning("Cannot send notification, FromEmail not configured");
            return;
        }

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            Credentials = new System.Net.NetworkCredential(_smtp.User, _smtp.Password),
            EnableSsl = _smtp.UseSsl
        };

        var fromAddress = new MailAddress(_smtp.FromEmail, _smtp.FromName);
        var message = new MailMessage
        {
            From = fromAddress,
            Subject = "Рассылка завершена",
            Body = "Все письма были успешно отправлены.",
            IsBodyHtml = false
        };
        message.To.Add(email);

        try
        {
            await client.SendMailAsync(message);
            _log.LogInformation("Notification sent to {email}", email);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send notification to {email}", email);
        }
    }
}

