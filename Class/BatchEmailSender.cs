using Microsoft.Extensions.Options;

namespace EmailMarketingService;

public class BatchEmailSender : BackgroundService
{
    private readonly ILogger<BatchEmailSender> _log;
    private readonly IEmailQueue _queue;
    private readonly IPendingStore _store;
    private readonly IServiceProvider _sp;
    private readonly IHttpClientFactory _http;
    private readonly SmtpOptions _smtp;

    private const int BatchSize = 400; // писем в сутки
    private readonly TimeSpan DelayBetweenBatches = TimeSpan.FromHours(24); // продолжение через 24 часа 
    private readonly string _notifyUponFinish = "gubinvs@gmail.com"; // уведомление после каждой итерации
    private readonly TimeSpan DelayBetweenEmails = TimeSpan.FromSeconds(3); // задержка между письмами

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

                    // Получаем все Pending письма
                    var pendingEmails = state.Pending.Where(p => !p.Sent).ToList();

                    if (pendingEmails.Any())
                    {
                        _log.LogInformation("Pending emails to send: {count}", pendingEmails.Count);

                        // Отправка батчами
                        foreach (var batch in pendingEmails.Chunk(BatchSize))
                        {
                            var body = await LoadEmailBody();

                            foreach (var item in batch)
                            {
                                await SendEmail(item.Email, body, stoppingToken);
                                item.Sent = true;
                                await Task.Delay(DelayBetweenEmails, stoppingToken);
                            }

                            // Сохраняем состояние после каждого батча
                            await _store.SaveAsync(state);
                        }

                        // Обновляем NextRunUtc после выполнения всех Pending
                        state.NextRunUtc = DateTime.UtcNow.Add(DelayBetweenBatches);
                        await _store.SaveAsync(state);
                    }
                    else
                    {
                        // Все письма отправлены, отправляем уведомление
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

    private async Task SendEmail(string email, string body, CancellationToken stoppingToken)
    {
        _log.LogInformation("Sending email to {email}", email);

        // Здесь можно добавить реальную отправку через SMTP
        await Task.Delay(50, stoppingToken); // имитация отправки
    }

    private async Task SendNotification(string email)
    {
        _log.LogInformation("Notification sent to {email}", email);
        await Task.CompletedTask;
    }
}
