namespace EmailMarketingService
{ 
    public class PendingEmail
    {
        public string Email { get; set; } = null!;
        public bool Sent { get; set; }
    }
}