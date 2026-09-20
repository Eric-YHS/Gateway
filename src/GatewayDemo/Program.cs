using System.Net;
using System.Security.Claims;
using GatewayDemo.Data;
using GatewayDemo.Extensions;
using GatewayDemo.Options;
using GatewayDemo.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

var gatewayOptions = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>() ?? new GatewayOptions();
gatewayOptions.Normalize();

var adminOptions = builder.Configuration.GetSection(AdminOptions.SectionName).Get<AdminOptions>() ?? new AdminOptions();
adminOptions.Normalize();

var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
storageOptions.Normalize(builder.Environment.ContentRootPath);

var dataProtectionOptions = builder.Configuration.GetSection(GatewayDataProtectionOptions.SectionName).Get<GatewayDataProtectionOptions>() ?? new GatewayDataProtectionOptions();
dataProtectionOptions.Normalize(builder.Environment.ContentRootPath);

if (string.IsNullOrWhiteSpace(adminOptions.PasswordHash))
{
    throw new InvalidOperationException("Admin password hash is not configured.");
}

builder.Services.AddSingleton(gatewayOptions);
builder.Services.AddSingleton(adminOptions);
builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton(dataProtectionOptions);
builder.Services.AddSingleton<GatewayHtmlRenderer>();
builder.Services.AddSingleton<AdminPasswordHasher>();
builder.Services.AddScoped<DeviceCredentialService>();
builder.Services.AddScoped<IGatewayRepository, GatewayRepository>();

Directory.CreateDirectory(dataProtectionOptions.KeyRingPath);
builder.Services
    .AddDataProtection()
    .SetApplicationName(dataProtectionOptions.ApplicationName)
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionOptions.KeyRingPath));

builder.Services.AddDbContext<GatewayDbContext>(options =>
{
    if (storageOptions.IsSqlServer)
    {
        options.UseSqlServer(storageOptions.SqlServerConnectionString);
    }
    else
    {
        options.UseSqlite(storageOptions.SqliteConnectionString);
    }
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var proxy in gatewayOptions.TrustedProxyAddresses)
    {
        if (IPAddress.TryParse(proxy, out var ipAddress))
        {
            options.KnownProxies.Add(ipAddress);
        }
    }

    foreach (var cidr in gatewayOptions.TrustedProxyCidrs)
    {
        var parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var prefixLength))
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
        }
    }
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = adminOptions.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(adminOptions.SessionHours);
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("admin-login", limiter =>
    {
        limiter.PermitLimit = adminOptions.LoginPermitLimit;
        limiter.Window = TimeSpan.FromMinutes(adminOptions.LoginWindowMinutes);
        limiter.QueueLimit = 0;
    });
});

builder.Services
    .AddReverseProxy()
    .LoadFromMemory(BuildProxyRoutes(gatewayOptions), BuildClusters(gatewayOptions));

var app = builder.Build();
await EnsureDatabaseAsync(app);

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self';";
    await next();
});

app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var deviceService = context.RequestServices.GetRequiredService<DeviceCredentialService>();
    var renderer = context.RequestServices.GetRequiredService<GatewayHtmlRenderer>();

    try
    {
        var deviceContext = await deviceService.GetOrCreateContextAsync(context, context.RequestAborted);
        context.SetDeviceContext(deviceContext);
        await next();
    }
    catch (DeviceCredentialException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        await context.Response.WriteAsync(renderer.RenderDeviceCredentialErrorPage(ex.Message));
    }
});

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments(gatewayOptions.ProxyBasePath, out var remainder))
    {
        await next();
        return;
    }

    var siteKey = ExtractSiteKey(remainder);
    if (string.IsNullOrWhiteSpace(siteKey) || gatewayOptions.FindSite(siteKey) is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        var renderer = context.RequestServices.GetRequiredService<GatewayHtmlRenderer>();
        await context.Response.WriteAsync(renderer.RenderNotFoundPage());
        return;
    }

    if (string.Equals(context.Request.Path.Value, $"{gatewayOptions.ProxyBasePath}/{siteKey}", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect($"{gatewayOptions.ProxyBasePath}/{siteKey}/");
        return;
    }

    var repository = context.RequestServices.GetRequiredService<IGatewayRepository>();
    var device = context.GetRequiredDeviceContext();
    var authorization = await repository.GetActiveAuthorizationAsync(device.DeviceId, context.RequestAborted);

    if (device.RequiresReview || authorization is null || !authorization.AllowsSite(siteKey))
    {
        var reason = device.RequiresReview
            ? $"阻止访问 {siteKey}，原因：{device.ChallengeReason}"
            : $"阻止访问 {siteKey}，设备未放行。";

        await repository.RecordAuditAsync(
            kind: device.RequiresReview ? "device-review-required" : "site-blocked",
            message: reason,
            device,
            siteKey,
            device.ClientIp,
            context.RequestAborted);

        var target = $"{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect($"/gateway?target={Uri.EscapeDataString(target)}");
        return;
    }

    await repository.TouchAuthorizationAsync(device.DeviceId, device.ClientIp, siteKey, context.RequestAborted);

    if (ShouldAuditAllowedRequest(context.Request))
    {
        await repository.RecordAuditAsync(
            kind: "site-allowed",
            message: $"放行访问 {siteKey}",
            device,
            siteKey,
            device.ClientIp,
            context.RequestAborted);
    }

    await next();
});

app.MapGet("/", () => Results.Redirect("/gateway"));

app.MapGet("/healthz", () =>
    Results.Json(new
    {
        ok = true,
        app = gatewayOptions.AppName,
        storage = storageOptions.Provider,
        siteCount = gatewayOptions.Sites.Count,
        managementPort = adminOptions.ManagementPort,
    }));

app.MapGet("/gateway", async (HttpContext context, IGatewayRepository repository, GatewayHtmlRenderer renderer) =>
{
    var device = context.GetRequiredDeviceContext();
    var target = ResolveTargetPath(context.Request.Query["target"], gatewayOptions);
    var targetSiteKey = ExtractSiteKeyFromTarget(target, gatewayOptions);
    var authorization = await repository.GetActiveAuthorizationAsync(device.DeviceId, context.RequestAborted);
    var latestRequest = await repository.GetLatestRequestAsync(device.DeviceId, context.RequestAborted);

    if (!device.RequiresReview && authorization is not null && authorization.AllowsSite(targetSiteKey))
    {
        await repository.TouchAuthorizationAsync(device.DeviceId, device.ClientIp, targetSiteKey, context.RequestAborted);
        return Results.Redirect(target);
    }

    return Results.Content(
        renderer.RenderGatewayPage(device, target, authorization, latestRequest),
        "text/html; charset=utf-8");
});

app.MapPost("/gateway/request-access", async (HttpContext context, IGatewayRepository repository, GatewayHtmlRenderer renderer) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var device = context.GetRequiredDeviceContext();
    var target = ResolveTargetPath(form["targetPath"], gatewayOptions);
    var companyName = form["companyName"].ToString().Trim();
    var applicantName = form["applicantName"].ToString().Trim();
    var phone = form["phone"].ToString().Trim();
    var reason = form["reason"].ToString().Trim();
    var requestedSiteKeys = NormalizeSiteKeys(form["siteKeys"], gatewayOptions);

    var latestRequest = await repository.GetLatestRequestAsync(device.DeviceId, context.RequestAborted);
    var authorization = await repository.GetActiveAuthorizationAsync(device.DeviceId, context.RequestAborted);

    if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(applicantName) || string.IsNullOrWhiteSpace(phone))
    {
        return Results.Content(
            renderer.RenderGatewayPage(
                device,
                target,
                authorization,
                latestRequest,
                "公司名称、申请人和联系电话为必填项。",
                companyName,
                applicantName,
                phone,
                reason,
                requestedSiteKeys),
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var normalizedTargetSiteKey = ExtractSiteKeyFromTarget(target, gatewayOptions);
    if (requestedSiteKeys.Count == 0)
    {
        requestedSiteKeys = [normalizedTargetSiteKey];
    }

    await repository.CreateOrUpdateRequestAsync(
        device,
        companyName,
        applicantName,
        phone,
        reason,
        target,
        requestedSiteKeys,
        context.RequestAborted);

    return Results.Redirect($"/gateway?target={Uri.EscapeDataString(target)}");
});

var adminSurface = app.MapGroup("/admin")
    .AddEndpointFilterFactory((_, next) => async invocationContext =>
    {
        var httpContext = invocationContext.HttpContext;
        return IsManagementRequest(httpContext, adminOptions)
            ? await next(invocationContext)
            : Results.NotFound();
    });

adminSurface.MapGet("/login", (ClaimsPrincipal user, GatewayHtmlRenderer renderer) =>
{
    if (user.Identity?.IsAuthenticated == true)
    {
        return Results.Redirect("/admin");
    }

    return Results.Content(renderer.RenderAdminLoginPage(), "text/html; charset=utf-8");
});

adminSurface.MapPost("/login", async (HttpContext context, GatewayHtmlRenderer renderer, AdminPasswordHasher hasher) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();

    if (!string.Equals(username, adminOptions.Username, StringComparison.Ordinal)
        || !hasher.VerifyHashedPassword(adminOptions.PasswordHash, password))
    {
        return Results.Content(
            renderer.RenderAdminLoginPage("用户名或密码不正确。"),
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, adminOptions.Username),
        new Claim(ClaimTypes.Role, "admin"),
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/admin");
}).RequireRateLimiting("admin-login");

var admin = adminSurface.MapGroup(string.Empty).RequireAuthorization();

admin.MapGet("/", async (IGatewayRepository repository, GatewayHtmlRenderer renderer) =>
{
    var snapshot = await repository.GetDashboardSnapshotAsync();
    return Results.Content(renderer.RenderAdminPage(snapshot), "text/html; charset=utf-8");
});

admin.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/admin/login");
});

admin.MapPost("/requests/{requestId:guid}/approve", async (Guid requestId, HttpContext context, IGatewayRepository repository) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var siteKeys = NormalizeSiteKeys(form["siteKeys"], gatewayOptions);
    var note = form["note"].ToString().Trim();
    await repository.ApproveRequestAsync(requestId, siteKeys, note, context.RequestAborted);
    return Results.Redirect("/admin");
});

admin.MapPost("/requests/{requestId:guid}/reject", async (Guid requestId, IGatewayRepository repository, HttpContext context) =>
{
    await repository.RejectRequestAsync(requestId, "管理员拒绝该次接入申请。", context.RequestAborted);
    return Results.Redirect("/admin");
});

admin.MapPost("/authorizations/{authorizationId:guid}/revoke", async (Guid authorizationId, IGatewayRepository repository, HttpContext context) =>
{
    await repository.RevokeAuthorizationAsync(authorizationId, "管理员手动撤销设备放行。", context.RequestAborted);
    return Results.Redirect("/admin");
});

admin.MapPost("/authorizations/{authorizationId:guid}/issue-app-credential", async (
    Guid authorizationId,
    GatewayDbContext dbContext,
    DeviceCredentialService deviceCredentialService,
    GatewayHtmlRenderer renderer,
    HttpContext context) =>
{
    var authorization = await dbContext.DeviceAuthorizations
        .AsNoTracking()
        .Include(x => x.AuthorizedSites)
        .FirstOrDefaultAsync(x => x.Id == authorizationId, context.RequestAborted);

    if (authorization is null)
    {
        return Results.NotFound();
    }

    var issued = await deviceCredentialService.IssueAppCredentialAsync(authorization.DeviceId, context.RequestAborted);
    return Results.Content(
        renderer.RenderIssuedAppCredentialPage(authorization, issued),
        "text/html; charset=utf-8");
});

app.MapReverseProxy();

app.MapFallback((GatewayHtmlRenderer renderer) =>
    Results.Content(renderer.RenderNotFoundPage(), "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound));

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    if (app.Environment.IsEnvironment("Testing"))
    {
        await db.Database.EnsureDeletedAsync();
    }

    await db.Database.EnsureCreatedAsync();
}

static IReadOnlyList<RouteConfig> BuildProxyRoutes(GatewayOptions options)
{
    return options.Sites
        .Select(site => new RouteConfig
        {
            RouteId = $"route-{site.Key}",
            ClusterId = $"cluster-{site.Key}",
            Match = new RouteMatch
            {
                Path = $"{options.ProxyBasePath}/{site.Key}/{{**catchall}}",
            },
            Transforms =
            [
                new Dictionary<string, string>
                {
                    ["PathRemovePrefix"] = $"{options.ProxyBasePath}/{site.Key}",
                },
                new Dictionary<string, string>
                {
                    ["RequestHeader"] = "X-Gateway-Site",
                    ["Set"] = site.Key,
                },
            ],
        })
        .ToArray();
}

static IReadOnlyList<ClusterConfig> BuildClusters(GatewayOptions options)
{
    return options.Sites
        .Select(site => new ClusterConfig
        {
            ClusterId = $"cluster-{site.Key}",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                [$"destination-{site.Key}"] = new()
                {
                    Address = site.UpstreamBaseUrl,
                },
            },
        })
        .ToArray();
}

static string ResolveTargetPath(string? requestedTarget, GatewayOptions options)
{
    if (string.IsNullOrWhiteSpace(requestedTarget))
    {
        return $"{options.ProxyBasePath}/{options.Sites[0].Key}/portal";
    }

    var decoded = requestedTarget.Trim();
    var siteKey = ExtractSiteKeyFromTarget(decoded, options);
    return options.FindSite(siteKey) is null
        ? $"{options.ProxyBasePath}/{options.Sites[0].Key}/portal"
        : decoded;
}

static string ExtractSiteKey(PathString remainder)
{
    var value = remainder.Value?.Trim('/');
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    var separator = value.IndexOf('/');
    return separator >= 0 ? value[..separator] : value;
}

static string ExtractSiteKeyFromTarget(string target, GatewayOptions options)
{
    if (!target.StartsWith(options.ProxyBasePath, StringComparison.OrdinalIgnoreCase))
    {
        return options.Sites[0].Key;
    }

    var value = target[options.ProxyBasePath.Length..].Trim('/');
    if (string.IsNullOrWhiteSpace(value))
    {
        return options.Sites[0].Key;
    }

    var separator = value.IndexOf('/');
    return separator >= 0 ? value[..separator] : value;
}

static List<string> NormalizeSiteKeys(Microsoft.Extensions.Primitives.StringValues values, GatewayOptions options)
{
    return values
        .SelectMany(item => (item ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Where(item => options.FindSite(item) is not null)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static bool ShouldAuditAllowedRequest(HttpRequest request)
{
    if (request.Method != HttpMethods.Get)
    {
        return false;
    }

    var accept = request.Headers.Accept.ToString();
    return string.IsNullOrWhiteSpace(accept)
        || accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
        || accept.Contains("*/*", StringComparison.OrdinalIgnoreCase);
}

static bool IsManagementRequest(HttpContext context, AdminOptions options)
{
    var localPort = context.Connection.LocalPort;
    if (localPort is 80 or 443 or 0)
    {
        return context.Request.Host.Port == options.ManagementPort;
    }

    return localPort == options.ManagementPort;
}

public partial class Program;
