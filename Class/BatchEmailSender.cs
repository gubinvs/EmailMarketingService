


namespace EmailMarketingService
{
    public class BatchEmailSender : BackgroundService
    {
        private readonly ILogger<BatchEmailSender> _log;
        private readonly IEmailQueue _queue;
        private readonly IPendingStore _store;
        private readonly IServiceProvider _sp;
        private readonly IHttpClientFactory _http;
        private readonly SmtpOptions _smtp;

        private const int BatchSize = 500;
        private readonly TimeSpan DelayBetweenBatches = TimeSpan.FromHours(24);
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

                    if (state.Pending.Any(p => !p.Sent))
                    {
                        var now = DateTime.UtcNow;

                        if (state.NextRunUtc == null || state.NextRunUtc <= now)
                        {
                            var batch = await _queue.DequeueBatch(BatchSize);

                            if (batch.Any())
                            {
                                _log.LogInformation("Sending batch of {n} emails", batch.Count);

                                // fetch body from a configured HTML URL
                                var body = await LoadEmailBody();
                                await SendBatch(batch.Select(b => b.Email), body);

                                // set next run
                                state.NextRunUtc = DateTime.UtcNow.Add(DelayBetweenBatches);
                                await _store.SaveAsync(state);
                            }
                        }
                        else
                        {
                            var wait = state.NextRunUtc.Value - DateTime.UtcNow;

                            if (wait > TimeSpan.Zero)
                            {
                                _log.LogInformation("Next run at {t}", state.NextRunUtc);
                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                            }
                        }
                    }
                    else
                    {
                        // nothing to send: if previously had items and now zero -> notify
                        if (state.Pending.All(p => p.Sent))
                        {
                            // send notification and clear
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error occurred in BatchEmailSender");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // avoid tight loop
            }
        }

        private async Task<string> LoadEmailBody()
        {
            // заглушка, загрузи HTML с URL или файла
            return await Task.FromResult("<html><body>Hello!</body></html>");
        }

        private async Task SendBatch(IEnumerable<string> emails, string body)
        {
            // заглушка, логика отправки писем
            foreach (var email in emails)
            {
                _log.LogInformation("Sending email to {email}", email);
                await Task.Delay(50); // имитируем отправку
            }
        }
    }
}
