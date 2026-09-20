using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GatewayDemo.Tests;

internal sealed class GatewayProcessHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly StringBuilder stdout = new();
    private readonly StringBuilder stderr = new();

    private GatewayProcessHost(Process process, int publicPort, int adminPort)
    {
        this.process = process;
        PublicPort = publicPort;
        AdminPort = adminPort;
    }

    public int PublicPort { get; }
    public int AdminPort { get; }

    public static async Task<GatewayProcessHost> StartAsync(
        string upstreamBaseUrl,
        string sqlitePath,
        string dataProtectionKeyRingPath,
        string adminPasswordHash)
    {
        var publicPort = GetFreePort();
        var adminPort = GetFreePort();
        var repoRoot = GetRepositoryRoot();

        var startInfo = new ProcessStartInfo
        {
            FileName = @"C:\Program Files\dotnet\dotnet.exe",
            Arguments = "\"src/GatewayDemo/bin/Debug/net10.0/GatewayDemo.dll\"",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{publicPort};http://127.0.0.1:{adminPort}";
        startInfo.Environment["Storage__Provider"] = "Sqlite";
        startInfo.Environment["Storage__SqliteConnectionString"] = $"Data Source={sqlitePath}";
        startInfo.Environment["DataProtection__ApplicationName"] = "GatewayDemo.Tests";
        startInfo.Environment["DataProtection__KeyRingPath"] = dataProtectionKeyRingPath;
        startInfo.Environment["Admin__Username"] = GatewayTestFixture.AdminUsername;
        startInfo.Environment["Admin__PasswordHash"] = adminPasswordHash;
        startInfo.Environment["Admin__ManagementPort"] = adminPort.ToString();
        startInfo.Environment["Gateway__Sites__0__Key"] = "erp-main";
        startInfo.Environment["Gateway__Sites__0__Name"] = "ERP Main";
        startInfo.Environment["Gateway__Sites__0__Description"] = "Test site";
        startInfo.Environment["Gateway__Sites__0__Accent"] = "#0f766e";
        startInfo.Environment["Gateway__Sites__0__UpstreamBaseUrl"] = $"{upstreamBaseUrl}mock/erp-main/";
        startInfo.Environment["Gateway__Sites__1__Key"] = "wms-east";
        startInfo.Environment["Gateway__Sites__1__Name"] = "WMS East";
        startInfo.Environment["Gateway__Sites__1__Description"] = "Test site";
        startInfo.Environment["Gateway__Sites__1__Accent"] = "#b45309";
        startInfo.Environment["Gateway__Sites__1__UpstreamBaseUrl"] = $"{upstreamBaseUrl}mock/wms-east/";
        startInfo.Environment["Gateway__Sites__2__Key"] = "finance";
        startInfo.Environment["Gateway__Sites__2__Name"] = "Finance";
        startInfo.Environment["Gateway__Sites__2__Description"] = "Test site";
        startInfo.Environment["Gateway__Sites__2__Accent"] = "#1d4ed8";
        startInfo.Environment["Gateway__Sites__2__UpstreamBaseUrl"] = $"{upstreamBaseUrl}mock/finance/";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var host = new GatewayProcessHost(process, publicPort, adminPort);
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                host.stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                host.stderr.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start gateway process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await host.WaitUntilHealthyAsync();
        return host;
    }

    public HttpClient CreatePublicClient() => CreateClient(PublicPort);

    public HttpClient CreateAdminClient() => CreateClient(AdminPort);

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }

        await process.WaitForExitAsync();
        process.Dispose();
    }

    private async Task WaitUntilHealthyAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PublicPort}") };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync("/healthz");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Retry until the process is ready.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Gateway process did not become healthy.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
    }

    private static HttpClient CreateClient(int port)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
