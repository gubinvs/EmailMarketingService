namespace EmailMarketingService
{ 
    public interface IPendingStore
    {
        Task<PendingState> LoadAsync(CancellationToken ct = default);
        Task SaveAsync(PendingState state, CancellationToken ct = default);
    }
} 