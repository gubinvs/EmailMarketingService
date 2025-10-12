using Microsoft.Extensions.Options;
using Newtonsoft.Json;


namespace EmailMarketingService
{
    public class JsonPendingStore : IPendingStore
    {
        private readonly StorageOptions _opt;
        private readonly ILogger<JsonPendingStore> _log;

        public JsonPendingStore(IOptions<StorageOptions> opt, ILogger<JsonPendingStore> log)
        {
            _opt = opt.Value;
            _log = log;
            EnsurePaths();
        }

        private void EnsurePaths()
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(_opt.StateFile)) ?? "./data";
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            var up = Path.GetFullPath(_opt.UploadFolder);
            if (!Directory.Exists(up)) Directory.CreateDirectory(up);
        }

        public async Task<PendingState> LoadAsync(CancellationToken ct = default)
        {
            if (!File.Exists(_opt.StateFile)) return new PendingState();
            var txt = await File.ReadAllTextAsync(_opt.StateFile, ct);
            return JsonConvert.DeserializeObject<PendingState>(txt) ?? new PendingState();
        }

        public async Task SaveAsync(PendingState state, CancellationToken ct = default)
        {
            var txt = JsonConvert.SerializeObject(state, Formatting.Indented);
            await File.WriteAllTextAsync(_opt.StateFile, txt, ct);
        }
    }
}