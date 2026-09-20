namespace GatewayDemo.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "Sqlite";
    public string SqliteConnectionString { get; set; } = "Data Source=App_Data/gateway-demo.db";
    public string SqlServerConnectionString { get; set; } = "Server=.;Database=GatewayDemo;Trusted_Connection=True;TrustServerCertificate=True;";

    public bool IsSqlServer => string.Equals(Provider, "SqlServer", StringComparison.OrdinalIgnoreCase);

    public void Normalize(string contentRoot)
    {
        Provider = string.IsNullOrWhiteSpace(Provider) ? "Sqlite" : Provider.Trim();

        if (!IsSqlServer)
        {
            const string prefix = "Data Source=";
            if (SqliteConnectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = SqliteConnectionString[prefix.Length..].Trim();
                if (!Path.IsPathRooted(value))
                {
                    var absolutePath = Path.Combine(contentRoot, value);
                    var directory = Path.GetDirectoryName(absolutePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    SqliteConnectionString = $"{prefix}{absolutePath}";
                }
            }
        }
    }
}
