namespace EmailMarketingService
{ 
    public interface IEmailQueue
    {
        Task EnqueueMany(IEnumerable<string> emails);
        Task<List<PendingEmail>> DequeueBatch(int batchSize);
        Task<int> CountPendingAsync();
    }
}