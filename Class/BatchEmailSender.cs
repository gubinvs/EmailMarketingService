using Microsoft.Extensions.Options;
using System.Net.Mail;
using HtmlAgilityPack;
using DocumentFormat.OpenXml.Drawing;

/// ✅ Исправлено залипание после лимита SMTP.
/// ✅ Добавлено уведомление, когда рассылка возобновляется после лимита.
/// ✅ Улучшены логи, структура и надёжность.
/// ✅ Заголовок и тело письма загружаются один раз за цикл.
/// ✅ Код готов к компиляции и использованию.
/// 

namespace EmailMarketingService;


public class BatchEmailSender : BackgroundService
{
    private readonly ILogger<BatchEmailSender> _log;
    private readonly IEmailQueue _queue;
    private readonly IPendingStore _store;
    private readonly IServiceProvider _sp;
    private readonly IHttpClientFactory _http;
    private readonly SmtpOptions _smtp;

    private readonly TimeSpan DelayBetweenEmails = TimeSpan.FromSeconds(80);
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var state = await _store.LoadAsync(stoppingToken);
                var pending = state.Pending.Where(p => !p.Sent).ToList();

                if (!pending.Any())
                {
                    _log.LogDebug("No pending emails to send.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                // 🔒 Проверяем, есть ли активный лимит
                if (state.NextRunUtc != null)
                {
                    if (state.NextRunUtc > DateTime.UtcNow)
                    {
                        var wait = state.NextRunUtc.Value - DateTime.UtcNow;
                        _log.LogInformation("SMTP limit in effect until {time} (waiting {minutes:F1} min)",
                            state.NextRunUtc, wait.TotalMinutes);

                        try
                        {
                            await Task.Delay(wait, stoppingToken);
                        }
                        catch (TaskCanceledException) { break; }

                        _log.LogInformation("SMTP limit expired. Resuming sending...");
                    }

                    // Сбрасываем лимит и уведомляем
                    state.NextRunUtc = null;
                    await _store.SaveAsync(state);

                    try
                    {
                        var remaining = state.Pending.Count(p => !p.Sent);
                        var body = $"<p>SMTP-лимит истёк, рассылка @gmail.com возобновлена.</p>" +
                                   $"<p>Время: {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>" +
                                   $"<p>Осталось писем для отправки: <b>{remaining}</b></p>";

                        await SendNotification(_notifyUponFinish, "Возобновлена рассылка", body, html: true);
                        _log.LogInformation("Sent resume notification to {email}", _notifyUponFinish);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to send resume notification");
                    }
                }

                // Загружаем тело и заголовок писем один раз
                var emailBody = await LoadEmailBody();
                var emailTitle = await LoadEmailTitle();

                foreach (var item in pending)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    try
                    {
                        await SendEmail(item.Email, emailBody, emailTitle);
                        item.Sent = true;
                        await _store.SaveAsync(state);
                        _log.LogInformation("Email successfully sent to {email}", item.Email);
                    }
                    catch (SmtpException ex) when (ex.Message.Contains("Limit per hour", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.LogWarning("SMTP limit reached. Postponing next run for 1 hour. ({msg})", ex.Message);
                        state.NextRunUtc = DateTime.UtcNow.AddHours(1);
                        await _store.SaveAsync(state);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to send email to {email}, will retry later", item.Email);
                    }

                    await Task.Delay(DelayBetweenEmails, stoppingToken);
                }

                // Уведомляем, если всё разослано
                if (state.Pending.All(p => p.Sent) && !state.NotificationSent)
                {
                    _log.LogInformation("All emails sent. Sending notification to {email}", _notifyUponFinish);

                    var body = "<p>Все письма были успешно отправлены.</p>" +
                               $"<p>Время завершения: {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>";

                    await SendNotification(_notifyUponFinish, "Рассылка @gmail.com завершена", body, html: true);
                    state.NotificationSent = true;
                    await _store.SaveAsync(state);
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error in BatchEmailSender loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        _log.LogInformation("BatchEmailSender stopped.");
    }

    // ===================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ =====================

    private async Task<string> LoadEmailBody()
    {
        string url = "https://encomponent.ru/email-body.html";
        //string url = "https://encomponent.ru/email-body_2.html";
        return await GetHtmlBodyAsync(url);
    }

    private async Task<string> LoadEmailTitle()
    {
        string url = "https://encomponent.ru/email-body.html";
        //string url = "https://encomponent.ru/email-body_2.html";
        return await ExtractTitleFromHtmlAsync(url);
    }

    private async Task<string> ExtractTitleFromHtmlAsync(string url)
    {
        using var client = _http.CreateClient();
        try
        {
            var html = await client.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            return titleNode?.InnerText?.Trim() ?? "Без темы";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ошибка при загрузке заголовка письма");
            return "Ошибка при загрузке заголовка";
        }
    }

    static async Task<string> GetHtmlBodyAsync(string url)
    {
        using var client = new HttpClient();
        try
        {
            return await client.GetStringAsync(url);
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"Ошибка запроса: {e.Message}");
            return "";
        }
    }

    private async Task SendEmail(string email, string body, string title)
    {
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
            Subject = title,
            Body = body,
            IsBodyHtml = true
        };
        message.To.Add(email);

        await client.SendMailAsync(message);
    }

    private async Task SendNotification(string email, string subject, string body, bool html = false)
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
            Subject = subject,
            Body = body,
            IsBodyHtml = html
        };
        message.To.Add(email);

        try
        {
            await client.SendMailAsync(message);
            _log.LogInformation("Notification sent to {email} ({subject})", email, subject);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send notification to {email}", email);
        }
    }
}
