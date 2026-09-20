using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace GatewayDemo.Tests;

internal sealed class FakeUpstreamServer : IAsyncDisposable
{
    private readonly WebApplication app;

    private FakeUpstreamServer(WebApplication app, string baseUrl)
    {
        this.app = app;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public static async Task<FakeUpstreamServer> StartAsync()
    {
        var port = GetFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(baseUrl);

        var app = builder.Build();

        app.MapGet("/healthz", () => Results.Json(new { ok = true }));

        app.MapGet("/mock/{siteKey}/api/ping", (HttpContext context, string siteKey) =>
            Results.Json(new
            {
                ok = true,
                siteKey,
                proxiedBy = context.Request.Headers["X-Gateway-Site"].ToString(),
            }));

        app.MapPost("/mock/{siteKey}/api/submit", async (HttpContext context, string siteKey) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            return Results.Json(new
            {
                ok = true,
                siteKey,
                body,
                proxiedBy = context.Request.Headers["X-Gateway-Site"].ToString(),
            });
        });

        app.MapGet("/mock/{siteKey}/{page?}", (HttpContext context, string siteKey, string? page) =>
        {
            var currentPage = string.IsNullOrWhiteSpace(page) ? "portal" : page;
            var html = $$"""
            <!DOCTYPE html>
            <html>
            <body>
              <h1>backend-ok</h1>
              <p id="site">{{WebUtility.HtmlEncode(siteKey)}}</p>
              <p id="page">{{WebUtility.HtmlEncode(currentPage)}}</p>
              <p id="proxy">{{WebUtility.HtmlEncode(context.Request.Headers["X-Gateway-Site"].ToString())}}</p>
            </body>
            </html>
            """;
            return Results.Content(html, "text/html; charset=utf-8");
        });

        await app.StartAsync();
        return new FakeUpstreamServer(app, $"{baseUrl}/");
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
