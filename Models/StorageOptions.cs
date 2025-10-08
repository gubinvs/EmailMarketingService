namespace EmailMarketingService
{ 
    public record StorageOptions
    {
        public string UploadFolder { get; init; } = "./uploads";
        public string StateFile { get; init; } = "./data/state.json";
    }
}