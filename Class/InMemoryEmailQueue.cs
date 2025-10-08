namespace EmailMarketingService
{ 
    public class InMemoryEmailQueue : IEmailQueue
    {
        private readonly IPendingStore _store;
        private readonly ILogger<InMemoryEmailQueue> _log;
        private readonly SemaphoreSlim _lock = new(1,1);


        public InMemoryEmailQueue(IPendingStore store, ILogger<InMemoryEmailQueue> log)
        {
            _store = store;
            _log = log;
        }


        public async Task EnqueueMany(IEnumerable<string> emails)
        {
            await _lock.WaitAsync();
            try
            {
                var state = await _store.LoadAsync();
                foreach (var e in emails)
                {
                    if (!state.Pending.Any(p => p.Email.Equals(e, StringComparison.OrdinalIgnoreCase)))
                        state.Pending.Add(new PendingEmail { Email = e, Sent = false });
                }
                await _store.SaveAsync(state);
            }
            finally { _lock.Release(); }
        }


        public async Task<List<PendingEmail>> DequeueBatch(int batchSize)
        {
            await _lock.WaitAsync();
            try
            {
                var state = await _store.LoadAsync();
                var batch = state.Pending.Where(p => !p.Sent).Take(batchSize).ToList();
                foreach (var b in batch) b.Sent = true; // mark as sent (will be actually sent by sender)
                await _store.SaveAsync(state);
                return batch;
            }
            finally { _lock.Release(); }
        }


        public async Task<int> CountPendingAsync()
        {
            var state = await _store.LoadAsync();
            return state.Pending.Count(p => !p.Sent);
        }
    }
}