namespace GatewayDemo.Options;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "gateway-admin";
    public string PasswordHash { get; set; } = string.Empty;
    public string CookieName { get; set; } = "gw_admin_session";
    public int ManagementPort { get; set; } = 5051;
    public int SessionHours { get; set; } = 8;
    public int LoginPermitLimit { get; set; } = 5;
    public int LoginWindowMinutes { get; set; } = 5;

    public void Normalize()
    {
        Username = string.IsNullOrWhiteSpace(Username) ? "gateway-admin" : Username.Trim();
        CookieName = string.IsNullOrWhiteSpace(CookieName) ? "gw_admin_session" : CookieName.Trim();
        ManagementPort = ManagementPort <= 0 ? 5051 : ManagementPort;
        SessionHours = SessionHours <= 0 ? 8 : SessionHours;
        LoginPermitLimit = LoginPermitLimit <= 0 ? 5 : LoginPermitLimit;
        LoginWindowMinutes = LoginWindowMinutes <= 0 ? 5 : LoginWindowMinutes;
    }
}
