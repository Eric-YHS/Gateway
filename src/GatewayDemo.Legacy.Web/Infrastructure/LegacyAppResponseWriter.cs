using System.Web;
using System.Web.Script.Serialization;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class LegacyAppResponseWriter
    {
        public static void WritePlainText(HttpContext context, string content)
        {
            context.Response.Clear();
            context.Response.StatusCode = 200;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            context.Response.Write(content ?? string.Empty);
        }

        public static void WriteGatewayReady(HttpContext context)
        {
            var serializer = new JavaScriptSerializer();
            context.Response.StatusCode = 200;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Write(serializer.Serialize(new
            {
                Status = 1,
                Code = 200,
                Msg = "网关已就绪",
                Message = "网关已就绪",
                GatewayReady = true
            }));
        }

        public static void WriteAuthorizationRequiredJson(HttpContext context, int statusCode, string message, string siteKey)
        {
            var serializer = new JavaScriptSerializer();
            context.Response.StatusCode = statusCode;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Write(serializer.Serialize(new
            {
                Status = 0,
                Code = statusCode,
                Msg = message,
                Message = message,
                GatewayBlocked = true,
                Site = siteKey ?? string.Empty
            }));
        }

        public static void WriteBlockedJson(HttpContext context, int originalStatusCode, string message, LegacyAppRequestDescriptor descriptor)
        {
            context.Response.StatusCode = 200;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["X-Gateway-Original-Status"] = originalStatusCode.ToString();
            context.Response.Write(BuildBlockedPayload(message, descriptor));
        }

        private static string BuildBlockedPayload(string message, LegacyAppRequestDescriptor descriptor)
        {
            var serializer = new JavaScriptSerializer();
            if (descriptor != null && descriptor.IsLoginRequest)
            {
                return serializer.Serialize(new
                {
                    LoginStatus = 0,
                    Status = 0,
                    Msg = message,
                    Message = message,
                    GatewayBlocked = true,
                    Applicant = descriptor.ApplicantName,
                    Company = descriptor.CompanyName,
                    DoActionResult = BuildDoActionResult(message)
                });
            }

            return serializer.Serialize(new
            {
                Status = 0,
                Code = 403,
                Msg = message,
                Message = message,
                GatewayBlocked = true,
                Applicant = descriptor == null ? string.Empty : descriptor.ApplicantName,
                Company = descriptor == null ? string.Empty : descriptor.CompanyName,
                Data = string.Empty,
                DoActionResult = BuildDoActionResult(message)
            });
        }

        private static object BuildDoActionResult(string message)
        {
            return new
            {
                Status = 0,
                MessageList = new[]
                {
                    new
                    {
                        Msg = message,
                        StackTrace = string.Empty
                    }
                },
                Data = string.Empty
            };
        }
    }
}
