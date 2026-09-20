using System.Web.Routing;

namespace GatewayDemo.Legacy.Web.App_Start
{
    public static class RouteConfig
    {
        public static void RegisterRoutes()
        {
            RouteTable.Routes.MapPageRoute(
                "GatewayRoot",
                string.Empty,
                "~/Gateway/Default.aspx");

            RouteTable.Routes.MapPageRoute(
                "GatewayPage",
                "gateway",
                "~/Gateway/Default.aspx");

            RouteTable.Routes.MapPageRoute(
                "AdminPage",
                "admin",
                "~/Admin/Default.aspx");
        }
    }
}
