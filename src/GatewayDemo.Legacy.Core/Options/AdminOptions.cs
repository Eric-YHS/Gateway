namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class AdminOptions
    {
        public const string SectionName = "Admin";

        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public int ManagementPort { get; set; }
        public int LoginPermitLimit { get; set; }
        public int LoginWindowMinutes { get; set; }

        public AdminOptions()
        {
            Username = "gateway-admin";
            PasswordHash = string.Empty;
            ManagementPort = 5051;
            LoginPermitLimit = 5;
            LoginWindowMinutes = 5;
        }
    }
}
