using System;
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
            WriteBlockedJson(
                context,
                originalStatusCode,
                message,
                descriptor,
                string.Empty,
                false,
                false,
                true);
        }

        public static void WriteBlockedJson(
            HttpContext context,
            int originalStatusCode,
            string message,
            LegacyAppRequestDescriptor descriptor,
            string gatewayReason,
            bool reapplyRequired,
            bool reloginRequired,
            bool includeClientNotification)
        {
            context.Response.StatusCode = 200;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.SuppressFormsAuthenticationRedirect = true;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["X-Gateway-Original-Status"] = originalStatusCode.ToString();
            context.Response.Headers["X-Gateway-Error-Source"] = "gateway";
            if (!string.IsNullOrWhiteSpace(gatewayReason))
            {
                context.Response.Headers["X-Gateway-Reason"] = gatewayReason;
            }
            ApplySensitiveResponseCachePolicy(context);
            context.Response.Write(BuildBlockedPayload(
                originalStatusCode,
                message,
                descriptor,
                gatewayReason,
                reapplyRequired,
                reloginRequired,
                includeClientNotification));
        }

        private static void ApplySensitiveResponseCachePolicy(HttpContext context)
        {
            context.Response.Cache.SetCacheability(HttpCacheability.Private);
            context.Response.Cache.SetNoStore();
            context.Response.Cache.SetRevalidation(HttpCacheRevalidation.AllCaches);
            context.Response.Headers["Cache-Control"] = "no-store, private";
            context.Response.Headers["Pragma"] = "no-cache";
        }

        private static string BuildBlockedPayload(
            int originalStatusCode,
            string message,
            LegacyAppRequestDescriptor descriptor,
            string gatewayReason,
            bool reapplyRequired,
            bool reloginRequired,
            bool includeClientNotification)
        {
            var serializer = new JavaScriptSerializer();
            var hasExplicitGatewayReason = !string.IsNullOrWhiteSpace(gatewayReason);
            var clientMessage = includeClientNotification ? message : string.Empty;
            if (descriptor != null && descriptor.IsLoginRequest)
            {
                if (hasExplicitGatewayReason)
                {
                    return serializer.Serialize(new
                    {
                        LoginStatus = 0,
                        Status = 0,
                        Code = originalStatusCode,
                        Msg = clientMessage,
                        Message = clientMessage,
                        GatewayMessage = clientMessage,
                        GatewayReason = gatewayReason,
                        GatewayBlocked = true,
                        ReapplyRequired = reapplyRequired,
                        ReloginRequired = reloginRequired,
                        ClientNotificationSuppressed = !includeClientNotification,
                        Applicant = descriptor.ApplicantName,
                        Company = descriptor.CompanyName,
                        DoActionResult = BuildDoActionResult(message, includeClientNotification)
                    });
                }

                return serializer.Serialize(new
                {
                    LoginStatus = 0,
                    Status = 0,
                    Code = originalStatusCode,
                    Msg = message,
                    Message = message,
                    GatewayBlocked = true,
                    Applicant = descriptor.ApplicantName,
                    Company = descriptor.CompanyName,
                    DoActionResult = BuildDoActionResult(message, true)
                });
            }

            if (hasExplicitGatewayReason)
            {
                return serializer.Serialize(new
                {
                    LoginStatus = 0,
                    Status = 0,
                    Code = originalStatusCode,
                    Msg = clientMessage,
                    Message = clientMessage,
                    GatewayMessage = clientMessage,
                    GatewayReason = gatewayReason,
                    GatewayBlocked = true,
                    ReapplyRequired = reapplyRequired,
                    ReloginRequired = reloginRequired,
                    ClientNotificationSuppressed = !includeClientNotification,
                    Applicant = descriptor == null ? string.Empty : descriptor.ApplicantName,
                    Company = descriptor == null ? string.Empty : descriptor.CompanyName,
                    Data = string.Empty,
                    DoActionResult = BuildDoActionResult(message, includeClientNotification)
                });
            }

            return serializer.Serialize(new
            {
                Status = 0,
                Code = originalStatusCode,
                Msg = message,
                Message = message,
                GatewayBlocked = true,
                Applicant = descriptor == null ? string.Empty : descriptor.ApplicantName,
                Company = descriptor == null ? string.Empty : descriptor.CompanyName,
                Data = string.Empty,
                DoActionResult = BuildDoActionResult(message, true)
            });
        }

        private static object BuildDoActionResult(string message, bool includeClientNotification)
        {
            var messages = includeClientNotification
                ? new object[]
                {
                    new
                    {
                        Msg = message,
                        StackTrace = string.Empty
                    }
                }
                : new object[0];

            return new
            {
                Status = 0,
                MessageList = messages,
                Data = string.Empty
            };
        }
    }
}
