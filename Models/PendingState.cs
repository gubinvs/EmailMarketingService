namespace EmailMarketingService
{
    public class PendingState
    {
        public List<PendingEmail> Pending { get; set; } = new();
        public DateTime? NextRunUtc { get; set; }
        public bool NotificationSent { get; set; } = false;
    }
}