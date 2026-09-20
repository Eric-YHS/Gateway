using System.Web;

namespace GatewayDemo.Legacy.Web.Handlers
{
    public class Healthz : IHttpHandler
    {
        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
            var runtime = Infrastructure.LegacyGatewayRuntime.Current;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Write(
                "{\"ok\":true,\"app\":\""
                + HttpUtility.JavaScriptStringEncode(runtime.Configuration.Gateway.AppName)
                + "\",\"framework\":\"net472\",\"siteCount\":"
                + runtime.Configuration.Gateway.Sites.Count.ToString()
                + ",\"managementPort\":"
                + runtime.Configuration.Admin.ManagementPort.ToString()
                + "}");
        }
    }
}
