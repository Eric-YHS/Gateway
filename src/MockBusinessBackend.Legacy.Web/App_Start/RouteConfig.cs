using System.Web.Routing;

namespace MockBusinessBackend.Legacy.Web.App_Start
{
    public static class RouteConfig
    {
        public static void RegisterRoutes()
        {
            RouteTable.Routes.MapPageRoute(
                "Root",
                string.Empty,
                "~/Default.aspx");

            RouteTable.Routes.MapPageRoute(
                "MockSiteDefault",
                "mock/{siteKey}",
                "~/Default.aspx");

            RouteTable.Routes.MapPageRoute(
                "JvsApsUiRoot",
                "jvs-aps-ui",
                "~/Default.aspx");

            RouteTable.Routes.MapPageRoute(
                "JvsApsUi",
                "jvs-aps-ui/{*spaPath}",
                "~/Default.aspx");

            RouteTable.Routes.MapPageRoute(
                "ApiSpaPingRoot",
                "api/spa/ping",
                "~/Default.aspx");

            RouteTable.Routes.MapPageRoute(
                "ApiRootPing",
                "api/ping",
                "~/ApiPing.aspx");

            RouteTable.Routes.MapPageRoute(
                "MockPing",
                "mock/{siteKey}/api/ping",
                "~/ApiPing.aspx");

            RouteTable.Routes.MapPageRoute(
                "MockSiteCatchAll",
                "mock/{siteKey}/{*pageInfo}",
                "~/Default.aspx");
        }
    }
}
