using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.UI;

namespace MockBusinessBackend.Legacy.Web
{
    public partial class ApiPing : Page
    {
        private static readonly IDictionary<string, string> SiteNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "erp-main", "ERP 主站" },
            { "wms-east", "WMS 东仓" },
            { "finance", "财务共享" }
        };

        protected void Page_Load(object sender, EventArgs e)
        {
            var siteKey = Convert.ToString(Page.RouteData.Values["siteKey"]);
            if (string.IsNullOrWhiteSpace(siteKey))
            {
                siteKey = "erp-main";
            }

            if (string.IsNullOrWhiteSpace(siteKey) || !SiteNames.ContainsKey(siteKey))
            {
                Response.StatusCode = 404;
                Response.ContentType = "application/json; charset=utf-8";
                Response.Write("{\"ok\":false}");
                return;
            }

            var payload = new
            {
                ok = true,
                siteKey = siteKey,
                siteName = SiteNames[siteKey],
                proxiedBy = Request.Headers["X-Gateway-Site"] ?? string.Empty,
                forwardedFor = Request.Headers["X-Forwarded-For"] ?? string.Empty,
                publicOrigin = Request.Headers["X-Gateway-Public-Origin"] ?? string.Empty,
                publicBase = Request.Headers["X-Gateway-Public-Base"] ?? string.Empty,
                proxyBase = Request.Headers["X-Gateway-Proxy-Base"] ?? string.Empty,
                proxySiteBase = Request.Headers["X-Gateway-Proxy-Site-Base"] ?? string.Empty,
                host = Request.Headers["Host"] ?? string.Empty,
                hasAuthorization = !string.IsNullOrEmpty(Request.Headers["Authorization"])
            };

            Response.ContentType = "application/json; charset=utf-8";
            Response.Write(new JavaScriptSerializer().Serialize(payload));
        }
    }
}
