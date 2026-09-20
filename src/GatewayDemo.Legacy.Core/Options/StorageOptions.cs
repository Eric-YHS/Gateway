namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class StorageOptions
    {
        public const string SectionName = "Storage";

        public string Provider { get; set; }
        public string SqliteConnectionString { get; set; }
        public string SqlServerConnectionString { get; set; }

        public StorageOptions()
        {
            Provider = "SqlServer";
            SqliteConnectionString = string.Empty;
            SqlServerConnectionString = string.Empty;
        }
    }
}
