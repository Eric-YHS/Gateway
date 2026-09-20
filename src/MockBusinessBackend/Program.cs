using System.Net;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var sites = new Dictionary<string, (string Name, string Description, string Accent)>
{
    ["erp-main"] = ("ERP 主站", "模拟 ERP 主业务系统，被网关放行后才可见。", "#0f766e"),
    ["wms-east"] = ("WMS 东仓", "模拟仓储业务站点，用于演示同设备跨站访问。", "#b45309"),
    ["finance"] = ("财务共享", "模拟财务审批站点，用于演示统一认证。", "#1d4ed8"),
};

app.MapGet("/", () => Results.Redirect("/mock/erp-main/portal"));

app.MapGet("/healthz", () => Results.Json(new
{
    ok = true,
    app = "MockBusinessBackend",
    siteCount = sites.Count,
}));

app.MapGet("/mock/{siteKey}/api/ping", (HttpContext context, string siteKey) =>
{
    if (!sites.ContainsKey(siteKey))
    {
        return Results.NotFound();
    }

    return Results.Json(new
    {
        ok = true,
        siteKey,
        siteName = sites[siteKey].Name,
        proxiedBy = context.Request.Headers["X-Gateway-Site"].ToString(),
        forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString(),
        host = context.Request.Headers.Host.ToString(),
    });
});

app.MapGet("/mock/{siteKey}/{page?}", (HttpContext context, string siteKey, string? page) =>
{
    if (!sites.TryGetValue(siteKey, out var site))
    {
        return Results.NotFound();
    }

    var currentPage = string.IsNullOrWhiteSpace(page) ? "portal" : page.Trim('/');
    var html = RenderPage(siteKey, site, currentPage, context);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

static string RenderPage(string siteKey, (string Name, string Description, string Accent) site, string page, HttpContext context)
{
    var title = page switch
    {
        "portal" => "业务入口",
        "reports" => "经营报表",
        "ops" => "运维台账",
        _ => page,
    };

    return $$"""
    <!DOCTYPE html>
    <html lang="zh-CN">
    <head>
      <meta charset="utf-8" />
      <meta name="viewport" content="width=device-width, initial-scale=1" />
      <title>{{E(site.Name)}} - {{E(title)}}</title>
      <style>
        :root { color-scheme: dark; --accent: {{site.Accent}}; }
        body {
          margin: 0;
          font-family: "Aptos", "Segoe UI Variable", sans-serif;
          background: linear-gradient(135deg, #08111e, #111f36);
          color: #e2e8f0;
        }
        .shell { max-width: 1080px; margin: 0 auto; padding: 32px; }
        .hero, .panel {
          border: 1px solid rgba(148,163,184,.16);
          border-radius: 24px;
          background: rgba(10,18,32,.88);
          box-shadow: 0 24px 48px rgba(2,8,23,.38);
        }
        .hero { padding: 28px; display: grid; grid-template-columns: 1.2fr .8fr; gap: 22px; margin-bottom: 22px; }
        .eyebrow { margin: 0; color: var(--accent); letter-spacing: .18em; text-transform: uppercase; font-size: .74rem; }
        h1, h2 { margin: 8px 0 14px; }
        p, small, li { color: #94a3b8; line-height: 1.65; }
        .nav, .links { display: flex; flex-wrap: wrap; gap: 10px; }
        .nav a, .links a {
          padding: 10px 14px;
          border-radius: 999px;
          border: 1px solid rgba(148,163,184,.18);
          color: #cbd5e1;
          text-decoration: none;
        }
        .nav a.active { background: color-mix(in srgb, var(--accent) 14%, transparent); border-color: color-mix(in srgb, var(--accent) 35%, transparent); }
        .grid { display: grid; grid-template-columns: repeat(2, minmax(0,1fr)); gap: 22px; }
        .panel { padding: 22px; }
        .kv { display: grid; gap: 10px; }
        .kv div {
          display: flex;
          justify-content: space-between;
          gap: 16px;
          padding: 12px 14px;
          border-radius: 14px;
          background: rgba(7,12,22,.7);
          border: 1px solid rgba(148,163,184,.1);
        }
        code {
          padding: 2px 8px;
          border-radius: 999px;
          background: rgba(15,23,42,.9);
          color: #bfdbfe;
        }
        @media (max-width: 900px) {
          .hero, .grid { grid-template-columns: 1fr; }
          .shell { padding: 20px; }
        }
      </style>
    </head>
    <body>
      <div class="shell">
        <section class="hero">
          <div>
            <p class="eyebrow">Mock Backend</p>
            <h1>{{E(site.Name)}} / {{E(title)}}</h1>
            <p>{{E(site.Description)}} 这个页面是后端应用自己返回的，不是网关本身的静态页，因此可以直观看到反向代理已经生效。</p>
            <div class="nav">
              <a class="{{(page == "portal" ? "active" : string.Empty)}}" href="./portal">业务入口</a>
              <a class="{{(page == "reports" ? "active" : string.Empty)}}" href="./reports">经营报表</a>
              <a class="{{(page == "ops" ? "active" : string.Empty)}}" href="./ops">运维台账</a>
              <a href="./api/ping">API Ping</a>
            </div>
          </div>
          <div class="panel">
            <p class="eyebrow">代理痕迹</p>
            <div class="kv">
              <div><span>网关站点头</span><code>{{E(context.Request.Headers["X-Gateway-Site"].ToString())}}</code></div>
              <div><span>X-Forwarded-For</span><code>{{E(context.Request.Headers["X-Forwarded-For"].ToString())}}</code></div>
              <div><span>Host</span><code>{{E(context.Request.Headers.Host.ToString())}}</code></div>
            </div>
          </div>
        </section>

        <section class="grid">
          <article class="panel">
            <p class="eyebrow">当前页面</p>
            <h2>{{E(title)}}</h2>
            <p>你现在看到的是 <code>/proxy/{{E(siteKey)}}/{{E(page)}}</code> 经过 YARP 转发后的结果。后续把这里替换成真实 ERP 或 WMS 即可。</p>
            <ul>
              <li>网关负责设备号识别、申请、审批和放行。</li>
              <li>后端应用继续保持自己的业务页面和接口。</li>
              <li>多站点场景下，只需要多配上游地址，不需要在每个站点重复做一套网关。</li>
            </ul>
          </article>

          <article class="panel">
            <p class="eyebrow">切换站点</p>
            <h2>跨站访问演示</h2>
            <p>只要当前设备已经被统一认证放行，就可以直接打开其他代理入口：</p>
            <div class="links">
              <a href="http://localhost:5050/proxy/erp-main/portal">ERP 主站</a>
              <a href="http://localhost:5050/proxy/wms-east/portal">WMS 东仓</a>
              <a href="http://localhost:5050/proxy/finance/portal">财务共享</a>
            </div>
          </article>
        </section>
      </div>
    </body>
    </html>
    """;
}

static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
