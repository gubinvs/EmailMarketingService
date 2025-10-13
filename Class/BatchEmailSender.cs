using Microsoft.Extensions.Options;
using System.Net.Mail;
using HtmlAgilityPack;
using DocumentFormat.OpenXml.Drawing;


namespace EmailMarketingService;

public class BatchEmailSender : BackgroundService
{
    private readonly ILogger<BatchEmailSender> _log;
    private readonly IEmailQueue _queue;
    private readonly IPendingStore _store;
    private readonly IServiceProvider _sp;
    private readonly IHttpClientFactory _http;
    private readonly SmtpOptions _smtp;

    private readonly TimeSpan DelayBetweenEmails = TimeSpan.FromSeconds(80); // задержка между письмами
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
                var pendingEmails = state.Pending.Where(p => !p.Sent).ToList();

                if (!pendingEmails.Any())
                {
                    _log.LogDebug("No pending emails, skipping iteration.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                // проверяем, не надо ли ждать из-за предыдущего лимита SMTP
                if (state.NextRunUtc != null && state.NextRunUtc > DateTime.UtcNow)
                {
                    var wait = state.NextRunUtc.Value - DateTime.UtcNow;
                    _log.LogInformation("Next attempt scheduled at {time}, waiting {seconds} sec", state.NextRunUtc, wait.TotalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds, 60)), stoppingToken);
                    continue;
                }

                _log.LogInformation("Pending emails to send: {count}", pendingEmails.Count);
                var body = await LoadEmailBody();

                foreach (var item in pendingEmails)
                {
                    try
                    {
                        await SendEmail(item.Email, body);
                        item.Sent = true;

                        await _store.SaveAsync(state); // сохраняем после успешной отправки
                    }
                    catch (SmtpException ex) when (ex.Message.Contains("Limit per hour"))
                    {
                        _log.LogWarning("SMTP limit reached. Next attempt will be in 1 hour. Details: {0}", ex.Message);
                        state.NextRunUtc = DateTime.UtcNow.AddHours(1); // откладываем на 1 час
                        await _store.SaveAsync(state);
                        break; // выходим из цикла отправки текущих писем
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to send email to {email}, will retry later", item.Email);
                    }

                    await Task.Delay(DelayBetweenEmails, stoppingToken);
                }

                // проверяем, остались ли письма со Sent = false
                if (!state.Pending.Any(p => !p.Sent) && !state.NotificationSent)
                {
                    _log.LogInformation("All emails successfully sent. Sending notification to {email}", _notifyUponFinish);
                    await SendNotification(_notifyUponFinish);

                    state.NotificationSent = true;
                    await _store.SaveAsync(state);
                }
            }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                _log.LogError(ex, "Error occurred in BatchEmailSender");
            }

            // обычная задержка между проверками очереди
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    // Тело письма
    private async Task<string> LoadEmailBody()
    {
        string url = "https://encomponent.ru/email-body.html";
        string body = await GetHtmlBodyAsync(url);

        return body;
        // return await Task.FromResult("<html><body>Hello!</body></html>");
    }

    
   // Заголовок письма
    private async Task<string> LoadEmailTitle()
    {
        string url = "https://encomponent.ru/email-body.html";
        string title = await ExtractTitleFromHtmlAsync(url); // ✅ теперь это async-метод
        return title;
    }


    // метод извлечения <title> из HTML по URL
    private async Task<string> ExtractTitleFromHtmlAsync(string url)
    {
        using var client = _http.CreateClient(); // Используем IHttpClientFactory (из DI)
        try
        {
            var html = await client.GetStringAsync(url);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            return titleNode?.InnerText?.Trim() ?? "Нет тега <title>";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ошибка при получении или разборе HTML для извлечения <title>");
            return "Ошибка при загрузке заголовка";
        }
    }


    static async Task<string> GetHtmlBodyAsync(string url)
    {
        using (HttpClient client = new HttpClient())
        {
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
    }


    private async Task SendEmail(string email, string body)
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
            Subject = await LoadEmailTitle(), // заголовок письма
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