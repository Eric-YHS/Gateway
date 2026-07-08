using System;
using System.Web;

namespace MockBusinessBackend.Legacy.Web
{
    public class Global : HttpApplication
    {
        internal const string RouteSiteKeyItemName = "Mock.RouteSiteKey";
        internal const string RoutePageInfoItemName = "Mock.RoutePageInfo";

        protected void Application_Start(object sender, EventArgs e)
        {
            App_Start.RouteConfig.RegisterRoutes();
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            var path = Request == null ? string.Empty : (Request.Path ?? string.Empty);
            if (IsSpaPath(path))
            {
                Context.Items[RoutePageInfoItemName] = path.TrimStart('/');
                Context.RewritePath("~/Default.aspx", false);
                return;
            }

            if (!path.StartsWith("/mock/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var remainder = path.Substring("/mock/".Length).Trim('/');
            if (string.IsNullOrWhiteSpace(remainder))
            {
                return;
            }

            var separatorIndex = remainder.IndexOf('/');
            var siteKey = separatorIndex < 0 ? remainder : remainder.Substring(0, separatorIndex);
            var pageInfo = separatorIndex < 0 || separatorIndex == remainder.Length - 1
                ? string.Empty
                : remainder.Substring(separatorIndex + 1);

            if (IsStaticOrDedicatedEndpoint(pageInfo))
            {
                return;
            }

            Context.Items[RouteSiteKeyItemName] = siteKey;
            Context.Items[RoutePageInfoItemName] = pageInfo;
            Context.RewritePath("~/Default.aspx", false);
        }

        private static bool IsStaticOrDedicatedEndpoint(string pageInfo)
        {
            if (string.IsNullOrWhiteSpace(pageInfo))
            {
                return false;
            }

            return string.Equals(pageInfo.Trim('/'), "api/ping", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSpaPath(string path)
        {
            var normalized = (path ?? string.Empty).Trim().TrimStart('/').Replace("\\", "/");
            return string.Equals(normalized, "jvs-aps-ui", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("jvs-aps-ui/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
