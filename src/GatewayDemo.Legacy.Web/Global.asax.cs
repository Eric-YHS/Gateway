using System;
using System.Text;
using System.Web;

namespace GatewayDemo.Legacy.Web
{
    public class Global : HttpApplication
    {
        protected void Application_Start(object sender, EventArgs e)
        {
            App_Start.RouteConfig.RegisterRoutes();
            var ignored = Infrastructure.LegacyGatewayRuntime.Current;
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            var context = HttpContext.Current;
            if (context == null || context.Response == null || context.Response.HeadersWritten)
            {
                return;
            }

            var exception = Server.GetLastError();
            Server.ClearError();

            System.Diagnostics.Trace.TraceError(
                "GatewayDemo Application_Error: url={0}, exception={1}",
                context.Request == null ? "(unknown)" : context.Request.RawUrl,
                exception == null ? "(null)" : exception.ToString());

            try
            {
                context.Response.Clear();
                context.Response.StatusCode = 500;
                context.Response.TrySkipIisCustomErrors = true;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentEncoding = Encoding.UTF8;

                // 不回显原始异常信息（可能含内部路径/连接串等）；详情已记录到 Trace。
                var message = "服务器处理请求时发生错误，请稍后重试或联系管理员。";
                string html;
                try
                {
                    html = Infrastructure.LegacyGatewayRuntime.Current.Renderer.RenderServerErrorPage(message);
                }
                catch (Exception)
                {
                    html = "<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\" /><title>服务器错误</title></head><body><h1>服务器处理请求时发生错误</h1><p>"
                        + HttpUtility.HtmlEncode(message)
                        + "</p></body></html>";
                }

                context.Response.Write(html);
                context.ApplicationInstance.CompleteRequest();
            }
            catch (Exception)
            {
                // 输出错误页自身失败时交回默认处理，不再抛出新的异常。
            }
        }
    }
}
