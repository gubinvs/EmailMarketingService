
namespace EmailMarketingService
{
    public record SmtpOptions
    {
        public string Host { get; init; } = null!;
        public int Port { get; init; }
        public bool UseSsl { get; init; }
        public string User { get; init; } = null!;
        public string Password { get; init; } = null!;
        public string FromName { get; init; } = null!;
        public string FromEmail { get; init; } = null!;
    }
}